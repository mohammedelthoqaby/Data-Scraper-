using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetAds;

public class GetAdsQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetAdsQuery, GetAdsResponseDto>
{
    public async Task<GetAdsResponseDto> Handle(GetAdsQuery request, CancellationToken ct)
    {
        var query = from a in readContext.Query<Ad>()
                    join c in readContext.Query<Competitor>() on a.CompetitorId equals c.Id
                    join i in readContext.Query<AdvertiserIdentity>() on a.IdentityId equals i.Id
                    select new
                    {
                        Ad = a,
                        Competitor = c,
                        Identity = i
                    };

        if (request.CompetitorId.HasValue)
        {
            query = query.Where(x => x.Ad.CompetitorId == request.CompetitorId.Value);
        }

        if (request.IdentityId.HasValue)
        {
            query = query.Where(x => x.Ad.IdentityId == request.IdentityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            query = query.Where(x => x.Ad.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<AdStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Ad.Status == status);
        }

        if (request.From.HasValue)
        {
            query = query.Where(x => x.Ad.LastSeenAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(x => x.Ad.FirstSeenAt <= request.To.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(x => x.Ad.LastSeenAt)
            .Take(500)
            .ToListAsync(ct);

        var productIds = rawItems
            .Where(x => x.Ad.ProductId.HasValue)
            .Select(x => x.Ad.ProductId!.Value)
            .Distinct()
            .ToList();

        var compProducts = productIds.Count > 0
            ? await readContext.Query<Product>()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<Guid, Product>();

        var matches = productIds.Count > 0
            ? await (
                from m in readContext.Query<ProductMatch>()
                where m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant
                join cv in readContext.Query<Variant>() on m.CompetitorVariantId equals cv.Id
                where productIds.Contains(cv.ProductId)
                join rv in readContext.Query<Variant>() on m.ReferenceVariantId equals rv.Id
                join rp in readContext.Query<Product>() on rv.ProductId equals rp.Id
                select new
                {
                    CompetitorProductId = cv.ProductId,
                    CompetitorVariantId = cv.Id,
                    ReferenceProductId = rp.Id,
                    ReferenceVariantId = rv.Id,
                    ReferenceTitle = rp.Title,
                    ReferenceUrl = rp.CanonicalUrl
                }
            ).ToListAsync(ct)
            : [];

        var variantIds = matches.Select(m => m.CompetitorVariantId)
            .Concat(matches.Select(m => m.ReferenceVariantId))
            .Distinct()
            .ToList();

        var latestObservations = variantIds.Count > 0
            ? await readContext.Query<PriceObservation>()
                .Where(po => variantIds.Contains(po.VariantId))
                .OrderByDescending(po => po.ObservedAt)
                .GroupBy(po => po.VariantId)
                .Select(g => g.First())
                .ToDictionaryAsync(po => po.VariantId, ct)
            : new Dictionary<Guid, PriceObservation>();

        var items = rawItems.Select(x =>
        {
            Guid? prodId = x.Ad.ProductId;
            string? prodTitle = null;
            string? prodUrl = null;
            Guid? refProdId = null;
            string? refTitle = null;
            string? refUrl = null;
            decimal? compPrice = null;
            decimal? refPrice = null;
            string? pricePosition = null;
            decimal? priceDiffPercent = null;

            if (prodId.HasValue && compProducts.TryGetValue(prodId.Value, out var cp))
            {
                prodTitle = cp.Title;
                prodUrl = cp.CanonicalUrl;

                var match = matches.FirstOrDefault(m => m.CompetitorProductId == prodId.Value);
                if (match != null)
                {
                    refProdId = match.ReferenceProductId;
                    refTitle = match.ReferenceTitle;
                    refUrl = match.ReferenceUrl;

                    latestObservations.TryGetValue(match.CompetitorVariantId, out var cObs);
                    latestObservations.TryGetValue(match.ReferenceVariantId, out var rObs);

                    compPrice = cObs?.Price;
                    refPrice = rObs?.Price;
                    bool refInStock = rObs?.InStock ?? true;

                    if (compPrice.HasValue && refPrice.HasValue && compPrice.Value > 0)
                    {
                        priceDiffPercent = Math.Round(((refPrice.Value - compPrice.Value) / compPrice.Value) * 100m, 1);

                        if (!refInStock)
                        {
                            pricePosition = "lazyBeanOutOfStock";
                        }
                        else if (refPrice.Value <= compPrice.Value)
                        {
                            pricePosition = "cheapest";
                        }
                        else if ((refPrice.Value - compPrice.Value) / compPrice.Value <= 0.05m)
                        {
                            pricePosition = "within 5%";
                        }
                        else
                        {
                            pricePosition = "more expensive";
                        }
                    }
                    else
                    {
                        pricePosition = "unmatched";
                    }
                }
                else
                {
                    pricePosition = "unmatched";
                }
            }

            return new AdDto(
                x.Ad.Id,
                x.Competitor.Id,
                x.Competitor.Key,
                x.Competitor.Name,
                x.Identity.Id,
                x.Identity.Name,
                x.Ad.CampaignId,
                x.Ad.ExternalAdId,
                x.Ad.Platform.ToString(),
                x.Ad.Title,
                x.Ad.Body,
                x.Ad.LinkUrl,
                x.Ad.SnapshotUrl,
                x.Ad.DeliveryStartDate,
                x.Ad.DeliveryStopDate,
                x.Ad.FirstSeenAt,
                x.Ad.LastSeenAt,
                x.Ad.Status.ToString(),
                x.Ad.CreativeHash,
                prodId,
                prodTitle,
                prodUrl,
                refProdId,
                refTitle,
                refUrl,
                compPrice,
                refPrice,
                pricePosition,
                priceDiffPercent);
        }).ToList();

        return new GetAdsResponseDto(totalCount, items);
    }
}
