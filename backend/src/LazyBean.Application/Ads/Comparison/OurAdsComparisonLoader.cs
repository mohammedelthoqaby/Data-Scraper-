using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Comparison;

// Loads our ads, the competitors' ads and the matched product pairs, then compares them. Our ads are the ads of
// reference competitors; every other competitor's ads are competitor ads.
public static class OurAdsComparisonLoader
{
    public static async Task<IReadOnlyList<OwnAdComparisonRow>> BuildAsync(IReadDbContext db, CancellationToken ct)
    {
        var competitors = await db.Query<Competitor>().AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.IsReference })
            .ToListAsync(ct);
        var referenceIds = competitors.Where(c => c.IsReference).Select(c => c.Id).ToHashSet();
        var names = competitors.ToDictionary(c => c.Id, c => c.Name);

        var ads = await db.Query<Ad>().AsNoTracking()
            .Where(a => a.Status == AdStatus.Active && a.ProductId != null)
            .Select(a => new
            {
                a.Id,
                a.CompetitorId,
                ProductId = a.ProductId!.Value,
                a.Title,
                a.Body,
                a.LinkUrl,
                a.SnapshotUrl,
            })
            .ToListAsync(ct);

        var matches = await (
                from m in db.Query<ProductMatch>().AsNoTracking()
                join cv in db.Query<Variant>().AsNoTracking() on m.CompetitorVariantId equals cv.Id
                join rv in db.Query<Variant>().AsNoTracking() on m.ReferenceVariantId equals rv.Id
                where m.Status != MatchStatus.Rejected
                select new { OurProductId = rv.ProductId, CompetitorProductId = cv.ProductId })
            .Distinct()
            .ToListAsync(ct);

        var productIds = ads.Select(a => a.ProductId)
            .Concat(matches.Select(m => m.OurProductId))
            .Concat(matches.Select(m => m.CompetitorProductId))
            .ToHashSet();

        var products = await db.Query<Product>().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.CompetitorId })
            .ToDictionaryAsync(p => p.Id, ct);

        var variants = await db.Query<Variant>().AsNoTracking()
            .Where(v => productIds.Contains(v.ProductId))
            .Select(v => new { v.Id, v.ProductId })
            .ToListAsync(ct);
        var variantIds = variants.Select(v => v.Id).ToHashSet();
        var latestPriceByVariant = (await db.Query<PriceObservation>().AsNoTracking()
                .Where(po => variantIds.Contains(po.VariantId))
                .OrderByDescending(po => po.ObservedAt)
                .Select(po => new { po.VariantId, po.Price })
                .ToListAsync(ct))
            .GroupBy(po => po.VariantId)
            .ToDictionary(g => g.Key, g => (decimal?)g.First().Price);
        var variantsByProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.Select(v => v.Id).ToList());

        decimal? StorePrice(Guid productId) =>
            variantsByProduct.TryGetValue(productId, out var ids)
                ? ids.Select(id => latestPriceByVariant.GetValueOrDefault(id)).FirstOrDefault(p => p.HasValue)
                : null;

        var ownAds = new List<OwnAdInput>();
        var competitorAds = new List<CompetitorAdInput>();
        foreach (var ad in ads)
        {
            if (!products.TryGetValue(ad.ProductId, out var product))
            {
                continue;
            }

            if (referenceIds.Contains(ad.CompetitorId))
            {
                ownAds.Add(new OwnAdInput(ad.Id, ad.ProductId, product.Title, ad.Title, ad.Body, ad.LinkUrl, ad.SnapshotUrl));
            }
            else
            {
                competitorAds.Add(new CompetitorAdInput(
                    ad.Id,
                    ad.CompetitorId,
                    names.GetValueOrDefault(ad.CompetitorId, string.Empty),
                    ad.ProductId,
                    ad.Title,
                    ad.Body,
                    ad.LinkUrl,
                    ad.SnapshotUrl));
            }
        }

        var pairs = new List<MatchedPairInput>();
        foreach (var match in matches)
        {
            if (!products.TryGetValue(match.OurProductId, out var ourProduct) ||
                !products.TryGetValue(match.CompetitorProductId, out var theirProduct))
            {
                continue;
            }

            // Only a reference product matched to a competitor product is a comparison pair.
            if (!referenceIds.Contains(ourProduct.CompetitorId) || referenceIds.Contains(theirProduct.CompetitorId))
            {
                continue;
            }

            pairs.Add(new MatchedPairInput(
                match.OurProductId,
                ourProduct.Title,
                StorePrice(match.OurProductId),
                theirProduct.CompetitorId,
                names.GetValueOrDefault(theirProduct.CompetitorId, string.Empty),
                match.CompetitorProductId,
                theirProduct.Title,
                StorePrice(match.CompetitorProductId)));
        }

        return OurAdsComparison.Build(ownAds, competitorAds, pairs);
    }
}
