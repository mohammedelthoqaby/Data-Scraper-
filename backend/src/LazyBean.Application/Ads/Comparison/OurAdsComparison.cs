using LazyBean.Application.Ads.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Ads.Comparison;

// An ad we run (the reference competitor) on one of our products.
public sealed record OwnAdInput(
    Guid AdId,
    Guid ProductId,
    string ProductTitle,
    string? AdTitle,
    string? AdBody,
    string? LinkUrl,
    string? SnapshotUrl,
    AdPlatform Platform = AdPlatform.Meta);

// An ad a competitor runs on one of its products.
public sealed record CompetitorAdInput(
    Guid AdId,
    Guid CompetitorId,
    string CompetitorName,
    Guid ProductId,
    string? AdTitle,
    string? AdBody,
    string? LinkUrl,
    string? SnapshotUrl,
    AdPlatform Platform = AdPlatform.Meta);

// One of our products matched to one of a competitor's products, with each side's catalog price as a fallback.
public sealed record MatchedPairInput(
    Guid OurProductId,
    string OurProductTitle,
    decimal? OurStorePrice,
    Guid CompetitorId,
    string CompetitorName,
    Guid CompetitorProductId,
    string CompetitorProductTitle,
    decimal? CompetitorStorePrice);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum OwnAdComparisonKind
{
    // We and the competitor both advertise the matched product.
    Both,

    // The competitor advertises a matched product we do not: an opportunity.
    CompetitorOnly,

    // We advertise a product that no competitor ad matches.
    OursOnly,
}

// One row per our product, competitor product and platform. OurPrice is the price per unit we advertise, or our
// catalog price when we do not advertise it. GapPercent is how far our price is above (+) or below (-) the competitor's.
public sealed record OwnAdComparisonRow(
    Guid OurProductId,
    string OurProductTitle,
    Guid? CompetitorProductId,
    string? CompetitorProductTitle,
    Guid? CompetitorId,
    string? CompetitorName,
    OwnAdComparisonKind Kind,
    int OurAdCount,
    int CompetitorAdCount,
    decimal? OurPrice,
    decimal? CompetitorPrice,
    string? OurAdUrl,
    string? CompetitorAdUrl,
    string? Platform = null,
    decimal? GapPercent = null)
{
    // We advertise this product at a higher price than the competitor's ad for the same product.
    public bool OurAdIsMoreExpensive =>
        Kind == OwnAdComparisonKind.Both && OurPrice is > 0 && CompetitorPrice is > 0 && OurPrice > CompetitorPrice;
}

// Compares our ads with competitor ads for the same matched product. Pure: no database access.
public static class OurAdsComparison
{
    public static IReadOnlyList<OwnAdComparisonRow> Build(
        IEnumerable<OwnAdInput> ownAds,
        IEnumerable<CompetitorAdInput> competitorAds,
        IEnumerable<MatchedPairInput> pairs)
    {
        var own = ownAds.GroupBy(a => (a.ProductId, a.Platform)).ToDictionary(g => g.Key, g => g.ToList());
        var competitor = competitorAds.GroupBy(a => (a.ProductId, a.Platform)).ToDictionary(g => g.Key, g => g.ToList());
        var ownPlatforms = own.Keys.GroupBy(k => k.ProductId).ToDictionary(g => g.Key, g => g.Select(k => k.Platform).ToList());
        var competitorPlatforms = competitor.Keys.GroupBy(k => k.ProductId).ToDictionary(g => g.Key, g => g.Select(k => k.Platform).ToList());

        var rows = new List<OwnAdComparisonRow>();
        var ourProductsWithAMatch = new HashSet<Guid>();

        foreach (var pair in pairs.DistinctBy(p => (p.OurProductId, p.CompetitorProductId)))
        {
            // One row per platform on which either side has ads for the pair.
            var platforms = ownPlatforms.GetValueOrDefault(pair.OurProductId, [])
                .Concat(competitorPlatforms.GetValueOrDefault(pair.CompetitorProductId, []))
                .Distinct()
                .ToList();

            foreach (var platform in platforms)
            {
                own.TryGetValue((pair.OurProductId, platform), out var ours);
                competitor.TryGetValue((pair.CompetitorProductId, platform), out var theirs);

                ourProductsWithAMatch.Add(pair.OurProductId);
                var kind = ours is not null && theirs is not null
                    ? OwnAdComparisonKind.Both
                    : theirs is not null ? OwnAdComparisonKind.CompetitorOnly : OwnAdComparisonKind.OursOnly;

                var ourPrice = UnitPriceOf(ours?.Select(a => (a.AdTitle, a.AdBody)), pair.OurStorePrice);
                var theirPrice = UnitPriceOf(theirs?.Select(a => (a.AdTitle, a.AdBody)), pair.CompetitorStorePrice);

                rows.Add(new OwnAdComparisonRow(
                    pair.OurProductId,
                    pair.OurProductTitle,
                    pair.CompetitorProductId,
                    pair.CompetitorProductTitle,
                    pair.CompetitorId,
                    pair.CompetitorName,
                    kind,
                    ours?.Count ?? 0,
                    theirs?.Count ?? 0,
                    ourPrice,
                    theirPrice,
                    ours?.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null),
                    theirs?.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null),
                    platform.ToString(),
                    GapPercent(ourPrice, theirPrice)));
            }
        }

        // Our advertised products that no competitor product matches are still ours to show, one row per platform.
        foreach (var entry in own.Where(kv => !ourProductsWithAMatch.Contains(kv.Key.ProductId)))
        {
            var ads = entry.Value;
            rows.Add(new OwnAdComparisonRow(
                entry.Key.ProductId,
                ads[0].ProductTitle,
                null,
                null,
                null,
                null,
                OwnAdComparisonKind.OursOnly,
                ads.Count,
                0,
                UnitPriceOf(ads.Select(a => (a.AdTitle, a.AdBody)), null),
                null,
                ads.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null),
                null,
                entry.Key.Platform.ToString(),
                null));
        }

        return rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.OurProductTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Platform, StringComparer.Ordinal)
            .ToList();
    }

    // The price per unit an ad advertises wins (a bundle is divided by its quantity); the catalog price is the fallback.
    private static decimal? UnitPriceOf(IEnumerable<(string? Title, string? Body)>? ads, decimal? storePrice)
    {
        var advertised = ads?
            .Select(a => AdPriceParser.ParseOffer(a.Title, a.Body))
            .FirstOrDefault(o => o is not null);
        return advertised?.UnitPrice ?? storePrice;
    }

    private static decimal? GapPercent(decimal? ours, decimal? theirs) =>
        ours is > 0 && theirs is > 0
            ? Math.Round((ours.Value - theirs.Value) / theirs.Value * 100m, 1, MidpointRounding.AwayFromZero)
            : null;
}
