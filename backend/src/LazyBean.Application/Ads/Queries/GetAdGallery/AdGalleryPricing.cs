using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetAdGallery;

// The product an ad is linked to and the prices involved, worked out the way the ads list does: the competitor's product,
// our product it matches, and the latest prices of both.
public static class AdGalleryPricing
{
    public sealed record Priced(
        Guid? ProductId,
        string? ProductTitle,
        Guid? OurProductId,
        string? OurProductTitle,
        decimal? CompetitorPrice,
        decimal? OurPrice)
    {
        public static Priced None { get; } = new(null, null, null, null, null, null);
    }

    public static async Task<Dictionary<Guid, Priced>> ResolveAsync(IReadDbContext readContext, IReadOnlyList<Ad> ads, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Priced>();
        var productIds = ads.Where(a => a.ProductId.HasValue).Select(a => a.ProductId!.Value).Distinct().ToList();
        if (productIds.Count == 0)
        {
            return result;
        }

        var titles = await readContext.Query<Product>()
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        var variants = await readContext.Query<Variant>()
            .AsNoTracking()
            .Where(v => productIds.Contains(v.ProductId))
            .Select(v => new { v.Id, v.ProductId })
            .ToListAsync(ct);

        var matches = await (
            from m in readContext.Query<ProductMatch>().AsNoTracking()
            where m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant
            join cv in readContext.Query<Variant>().AsNoTracking() on m.CompetitorVariantId equals cv.Id
            where productIds.Contains(cv.ProductId)
            join rv in readContext.Query<Variant>().AsNoTracking() on m.ReferenceVariantId equals rv.Id
            join rp in readContext.Query<Product>().AsNoTracking() on rv.ProductId equals rp.Id
            select new
            {
                CompetitorProductId = cv.ProductId,
                CompetitorVariantId = cv.Id,
                ReferenceProductId = rp.Id,
                ReferenceVariantId = rv.Id,
                ReferenceTitle = rp.Title,
            }).ToListAsync(ct);

        var variantIds = matches.Select(m => m.CompetitorVariantId)
            .Concat(matches.Select(m => m.ReferenceVariantId))
            .Concat(variants.Select(v => v.Id))
            .Distinct()
            .ToList();

        var latest = variantIds.Count == 0
            ? new Dictionary<Guid, (decimal Price, bool InStock)>()
            : await readContext.Query<PriceObservation>()
                .AsNoTracking()
                .Where(po => variantIds.Contains(po.VariantId))
                .GroupBy(po => po.VariantId)
                .Select(g => g.OrderByDescending(po => po.ObservedAt)
                    .Select(po => new { po.VariantId, po.Price, po.InStock })
                    .First())
                .ToDictionaryAsync(po => po.VariantId, po => (po.Price, po.InStock ?? true), ct);

        foreach (var ad in ads)
        {
            if (ad.ProductId is not { } productId)
            {
                continue;
            }

            titles.TryGetValue(productId, out var title);
            var match = matches.FirstOrDefault(m => m.CompetitorProductId == productId);

            decimal? competitorPrice = null;
            decimal? ourPrice = null;
            Guid? ourProductId = null;
            string? ourTitle = null;
            if (match is not null)
            {
                ourProductId = match.ReferenceProductId;
                ourTitle = match.ReferenceTitle;
                if (latest.TryGetValue(match.CompetitorVariantId, out var competitor))
                {
                    competitorPrice = competitor.Price;
                }

                if (latest.TryGetValue(match.ReferenceVariantId, out var ours))
                {
                    ourPrice = ours.Price;
                }
            }
            else
            {
                // Not matched to ours: the store's own price of the product, when a variant has one.
                competitorPrice = variants
                    .Where(v => v.ProductId == productId)
                    .Select(v => latest.TryGetValue(v.Id, out var observed) && observed.Price > 0 ? (decimal?)observed.Price : null)
                    .FirstOrDefault(p => p is not null);
            }

            result[ad.Id] = new Priced(productId, title, ourProductId, ourTitle, competitorPrice, ourPrice);
        }

        return result;
    }
}
