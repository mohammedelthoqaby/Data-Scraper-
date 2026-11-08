using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetAdGallery;

// The ads gallery: one page of cards, newest first (or by store). A card is a group of identical ads from one store (the same
// text and the same first image); the card is its newest ad. Our own ads are the reference store's; the competitor ads are
// the others. A Google ad is Active only while it was shown within the active window.
// CountsOnly returns the totals with no cards. The Ads page uses it for its headline numbers.
public sealed record GetAdGalleryQuery(
    Guid? CompetitorId,
    string? Platform,
    string? Status,
    bool? HasPrice,
    bool? LinkedToOurs,
    string? Search,
    bool SortByStore,
    bool CountsOnly = false) : IRequest<AdGalleryResponseDto>;

public sealed record GetAdGalleryDetailQuery(Guid AdId) : IRequest<AdGalleryDetailDto?>;

public sealed class GetAdGalleryQueryHandler(IReadDbContext readContext, AdsOptions options)
    : IRequestHandler<GetAdGalleryQuery, AdGalleryResponseDto>
{
    // At most this many ads are read for one request, newest first. Older ads fall outside the gallery.
    public const int MaxCandidates = 5000;

    public async Task<AdGalleryResponseDto> Handle(GetAdGalleryQuery request, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(options.GoogleActiveWindowDays);
        var ads = await AdGalleryQueries.LoadAsync(
            readContext, request.CompetitorId, request.Platform, request.Status, request.HasPrice, request.Search, cutoff, MaxCandidates, ct);

        var priced = await AdGalleryPricing.ResolveAsync(readContext, ads.Select(a => a.Ad).ToList(), ct);
        var images = await AdGalleryQueries.ImageFingerprintsAsync(readContext, ads.Select(a => a.Ad.Id).ToList(), ct);
        var entries = ads
            .Select(a => a with
            {
                ImageSha256 = images.GetValueOrDefault(a.Ad.Id),
                Priced = priced.GetValueOrDefault(a.Ad.Id) ?? AdGalleryPricing.Priced.None,
            })
            .ToList();

        if (request.LinkedToOurs == true)
        {
            entries = entries.Where(e => e.Priced.OurProductId is not null).ToList();
        }

        var cards = entries
            .GroupBy(e => AdGalleryQueries.GroupKey(e))
            .Select(g => AdGalleryQueries.ToCard(g.OrderByDescending(e => e.Ad.LastSeenAt).First(), g.ToList()))
            .ToList();

        var ordered = request.SortByStore
            ? cards.OrderBy(c => c.CompetitorName, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(c => c.LastSeenAt).ToList()
            : cards.OrderByDescending(c => c.LastSeenAt).ToList();

        // Every matching card goes out in one response. The page renders them all, and the browser lazy-loads the images.
        var totalAds = cards.Sum(c => c.VariationCount);
        var truncated = ads.Count >= MaxCandidates;
        return new AdGalleryResponseDto(ordered.Count, totalAds, truncated, request.CountsOnly ? new List<AdGalleryCardDto>() : ordered);
    }
}

public sealed class GetAdGalleryDetailQueryHandler(IReadDbContext readContext, AdsOptions options)
    : IRequestHandler<GetAdGalleryDetailQuery, AdGalleryDetailDto?>
{
    public async Task<AdGalleryDetailDto?> Handle(GetAdGalleryDetailQuery request, CancellationToken ct)
    {
        var anchor = await readContext.Query<Ad>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AdId, ct);
        if (anchor is null)
        {
            return null;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(options.GoogleActiveWindowDays);
        var storeAds = await AdGalleryQueries.LoadAsync(
            readContext, anchor.CompetitorId, null, null, null, null, cutoff, GetAdGalleryQueryHandler.MaxCandidates, ct);
        var images = await AdGalleryQueries.ImageFingerprintsAsync(readContext, storeAds.Select(a => a.Ad.Id).ToList(), ct);
        var withImages = storeAds.Select(a => a with { ImageSha256 = images.GetValueOrDefault(a.Ad.Id) }).ToList();

        var anchorEntry = withImages.FirstOrDefault(a => a.Ad.Id == request.AdId);
        if (anchorEntry is null)
        {
            return null;
        }

        var key = AdGalleryQueries.GroupKey(anchorEntry);
        var group = withImages
            .Where(a => AdGalleryQueries.GroupKey(a) == key)
            .OrderByDescending(a => a.Ad.LastSeenAt)
            .ToList();

        var priced = await AdGalleryPricing.ResolveAsync(readContext, group.Select(g => g.Ad).ToList(), ct);
        var pricedGroup = group
            .Select(g => g with { Priced = priced.GetValueOrDefault(g.Ad.Id) ?? AdGalleryPricing.Priced.None })
            .ToList();

        // The card's own ad is the newest of the group. The gallery read does not carry these columns, so the detail reads them
        // for that ad only: its ad ID, why it is unlinked, its delivery dates, and the advertiser ID of its identity.
        var representative = pricedGroup[0];
        var row = await readContext.Query<Ad>()
            .AsNoTracking()
            .Where(a => a.Id == representative.Ad.Id)
            .Select(a => new { a.ExternalAdId, a.IdentityId, a.LinkReason, a.DeliveryStartDate, a.DeliveryStopDate, a.Placements })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var advertiserId = await readContext.Query<AdvertiserIdentity>()
            .AsNoTracking()
            .Where(i => i.Id == row.IdentityId)
            .Select(i => i.ExternalId)
            .FirstOrDefaultAsync(ct);

        var facts = new AdGalleryFactsDto(
            row.ExternalAdId,
            string.IsNullOrEmpty(advertiserId) ? null : advertiserId,
            row.LinkReason,
            row.DeliveryStartDate,
            row.DeliveryStopDate,
            AdPlacements.Parse(row.Placements));

        return new AdGalleryDetailDto(
            AdGalleryQueries.ToCard(representative, pricedGroup),
            pricedGroup.Select(AdGalleryQueries.ToVariation).ToList(),
            facts);
    }
}

// One ad with what the gallery needs: its store, its effective status, its first-image fingerprint and its price link.
internal sealed record GalleryAd(
    Ad Ad,
    Guid CompetitorId,
    string CompetitorName,
    bool IsReference,
    string Status,
    string? ImageSha256,
    AdGalleryPricing.Priced Priced);

internal static class AdGalleryQueries
{
    public static async Task<List<GalleryAd>> LoadAsync(
        IReadDbContext readContext,
        Guid? competitorId,
        string? platform,
        string? status,
        bool? hasPrice,
        string? search,
        DateTimeOffset cutoff,
        int maxCandidates,
        CancellationToken ct)
    {
        var query = from a in readContext.Query<Ad>().AsNoTracking()
                    join c in readContext.Query<Competitor>().AsNoTracking() on a.CompetitorId equals c.Id
                    select new { Ad = a, Competitor = c };

        query = competitorId.HasValue
            ? query.Where(x => x.Ad.CompetitorId == competitorId.Value)
            : query.Where(x => !x.Competitor.IsReference);

        if (!string.IsNullOrWhiteSpace(platform) && Enum.TryParse<AdPlatform>(platform, true, out var adPlatform))
        {
            query = query.Where(x => x.Ad.Platform == adPlatform);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AdStatus>(status, true, out var adStatus))
        {
            query = adStatus switch
            {
                AdStatus.Active => query.Where(x => x.Ad.Status == AdStatus.Active &&
                                                    (x.Ad.Platform != AdPlatform.Google || x.Ad.LastSeenAt >= cutoff)),
                AdStatus.Inactive => query.Where(x => x.Ad.Status == AdStatus.Inactive ||
                                                      (x.Ad.Platform == AdPlatform.Google && x.Ad.Status == AdStatus.Active && x.Ad.LastSeenAt < cutoff)),
                _ => query.Where(x => x.Ad.Status == adStatus),
            };
        }

        if (hasPrice == true)
        {
            query = query.Where(x => x.Ad.AdvertisedPrice != null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.Ad.Title ?? string.Empty).ToLower().Contains(term) ||
                (x.Ad.Headline ?? string.Empty).ToLower().Contains(term) ||
                (x.Ad.Body ?? string.Empty).ToLower().Contains(term) ||
                (x.Ad.Description ?? string.Empty).ToLower().Contains(term) ||
                x.Competitor.Name.ToLower().Contains(term));
        }

        // Only the columns the gallery reads. RawDataJson and PreviewVariationsJson are large and stay in the database,
        // so a request that matches thousands of ads stays small in memory.
        var rows = await query
            .OrderByDescending(x => x.Ad.LastSeenAt)
            .Take(maxCandidates)
            .Select(x => new
            {
                x.Ad.Id,
                x.Ad.CompetitorId,
                x.Ad.Platform,
                x.Ad.Status,
                x.Ad.Title,
                x.Ad.Headline,
                x.Ad.Body,
                x.Ad.Description,
                x.Ad.LinkUrl,
                x.Ad.SnapshotUrl,
                x.Ad.CreativeFormat,
                x.Ad.ThumbnailCount,
                x.Ad.AdvertisedPrice,
                x.Ad.AdvertisedQuantity,
                x.Ad.FirstSeenAt,
                x.Ad.LastSeenAt,
                x.Ad.ProductId,
                StoreId = x.Competitor.Id,
                StoreName = x.Competitor.Name,
                StoreIsReference = x.Competitor.IsReference,
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var ad = new Ad
                {
                    Id = r.Id,
                    CompetitorId = r.CompetitorId,
                    Platform = r.Platform,
                    Status = r.Status,
                    Title = r.Title,
                    Headline = r.Headline,
                    Body = r.Body,
                    Description = r.Description,
                    LinkUrl = r.LinkUrl,
                    SnapshotUrl = r.SnapshotUrl,
                    CreativeFormat = r.CreativeFormat,
                    ThumbnailCount = r.ThumbnailCount,
                    AdvertisedPrice = r.AdvertisedPrice,
                    AdvertisedQuantity = r.AdvertisedQuantity,
                    FirstSeenAt = r.FirstSeenAt,
                    LastSeenAt = r.LastSeenAt,
                    ProductId = r.ProductId,
                };

                return new GalleryAd(
                    ad,
                    r.StoreId,
                    r.StoreName,
                    r.StoreIsReference,
                    EffectiveStatus(ad, cutoff),
                    null,
                    AdGalleryPricing.Priced.None);
            })
            .ToList();
    }

    // The first image of each ad, as a fingerprint (its SHA-256). Ads with no image have none.
    public static async Task<Dictionary<Guid, string>> ImageFingerprintsAsync(IReadDbContext readContext, List<Guid> adIds, CancellationToken ct)
    {
        if (adIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await readContext.Query<AdThumbnail>()
            .AsNoTracking()
            .Where(t => t.Position == 0 && adIds.Contains(t.AdId))
            .Select(t => new { t.AdId, t.Sha256 })
            .ToDictionaryAsync(t => t.AdId, t => t.Sha256, ct);
    }

    public static string GroupKey(GalleryAd entry) =>
        AdGalleryGrouping.Key(entry.Ad.Id, entry.CompetitorId, AdGalleryText.Headline(entry.Ad), AdGalleryText.Body(entry.Ad), entry.ImageSha256);

    public static AdGalleryCardDto ToCard(GalleryAd representative, IReadOnlyList<GalleryAd> members)
    {
        var ad = representative.Ad;
        var priced = representative.Priced;

        // A bundle is compared per unit. The gap is our price against what the ad advertises (or the store price when it does not).
        var unitPrice = ad.AdvertisedPrice is { } advertised
            ? Math.Round(advertised / (ad.AdvertisedQuantity ?? 1), 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var baseline = unitPrice ?? priced.CompetitorPrice;
        decimal? gap = priced.OurPrice is { } ours && baseline is { } basePrice && basePrice > 0
            ? Math.Round((ours - basePrice) / basePrice * 100m, 1)
            : null;

        return new AdGalleryCardDto(
            ad.Id,
            representative.CompetitorId,
            representative.CompetitorName,
            representative.IsReference,
            ad.Platform.ToString(),
            members.Any(m => m.Status == AdStatus.Active.ToString()) ? AdStatus.Active.ToString() : AdStatus.Inactive.ToString(),
            AdGalleryText.Headline(ad),
            AdGalleryText.Body(ad),
            ad.CreativeFormat,
            ad.ThumbnailCount,
            members.Count,
            members.Min(m => m.Ad.FirstSeenAt),
            members.Max(m => m.Ad.LastSeenAt),
            ad.SnapshotUrl,
            ad.AdvertisedPrice,
            ad.AdvertisedQuantity,
            unitPrice,
            priced.ProductId,
            priced.ProductTitle,
            priced.OurProductId,
            priced.OurProductTitle,
            priced.OurPrice,
            priced.CompetitorPrice,
            gap,
            representative.ImageSha256);
    }

    public static AdGalleryVariationDto ToVariation(GalleryAd entry) => new(
        entry.Ad.Id,
        AdGalleryText.Headline(entry.Ad),
        AdGalleryText.Body(entry.Ad),
        entry.Ad.LinkUrl,
        entry.Ad.SnapshotUrl,
        entry.Status,
        entry.Ad.CreativeFormat,
        entry.Ad.ThumbnailCount,
        entry.Ad.FirstSeenAt,
        entry.Ad.LastSeenAt);

    private static string EffectiveStatus(Ad ad, DateTimeOffset cutoff) =>
        ad.Status == AdStatus.Active && ad.Platform == AdPlatform.Google && ad.LastSeenAt < cutoff
            ? AdStatus.Inactive.ToString()
            : ad.Status.ToString();
}
