using LazyBean.Application.Ads.Common;

namespace LazyBean.Application.Ads.Comparison;

// An ad we run (the reference competitor) on one of our products.
public sealed record OwnAdInput(
    Guid AdId,
    Guid ProductId,
    string ProductTitle,
    string? AdTitle,
    string? AdBody,
    string? LinkUrl,
    string? SnapshotUrl);

// An ad a competitor runs on one of its products.
public sealed record CompetitorAdInput(
    Guid AdId,
    Guid CompetitorId,
    string CompetitorName,
    Guid ProductId,
    string? AdTitle,
    string? AdBody,
    string? LinkUrl,
    string? SnapshotUrl);

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
    string? CompetitorAdUrl)
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
        var own = ownAds.GroupBy(a => a.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var competitor = competitorAds.GroupBy(a => a.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<OwnAdComparisonRow>();
        var ourProductsWithAMatch = new HashSet<Guid>();

        foreach (var pair in pairs.DistinctBy(p => (p.OurProductId, p.CompetitorProductId)))
        {
            own.TryGetValue(pair.OurProductId, out var ours);
            competitor.TryGetValue(pair.CompetitorProductId, out var theirs);
            if (ours is null && theirs is null)
            {
                continue;
            }

            ourProductsWithAMatch.Add(pair.OurProductId);
            var kind = ours is not null && theirs is not null
                ? OwnAdComparisonKind.Both
                : theirs is not null ? OwnAdComparisonKind.CompetitorOnly : OwnAdComparisonKind.OursOnly;

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
                ours is null ? null : PriceOf(ours.Select(a => (a.AdTitle, a.AdBody)), pair.OurStorePrice),
                theirs is null ? null : PriceOf(theirs.Select(a => (a.AdTitle, a.AdBody)), pair.CompetitorStorePrice),
                ours?.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null),
                theirs?.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null)));
        }

        // Our advertised products that no competitor product matches are still ours to show.
        foreach (var (productId, ads) in own.Where(kv => !ourProductsWithAMatch.Contains(kv.Key)))
        {
            rows.Add(new OwnAdComparisonRow(
                productId,
                ads[0].ProductTitle,
                null,
                null,
                null,
                null,
                OwnAdComparisonKind.OursOnly,
                ads.Count,
                0,
                PriceOf(ads.Select(a => (a.AdTitle, a.AdBody)), null),
                null,
                ads.Select(a => a.SnapshotUrl ?? a.LinkUrl).FirstOrDefault(u => u is not null),
                null));
        }

        return rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.OurProductTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The price an ad advertises wins; the catalog price is the fallback when no ad text states one.
    private static decimal? PriceOf(IEnumerable<(string? Title, string? Body)> ads, decimal? storePrice)
    {
        var advertised = ads
            .Select(a => AdPriceParser.ParseAdvertisedPrice(a.Title, a.Body))
            .FirstOrDefault(p => p.HasValue);
        return advertised ?? storePrice;
    }
}
