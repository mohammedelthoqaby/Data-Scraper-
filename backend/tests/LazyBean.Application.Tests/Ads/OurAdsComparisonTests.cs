using LazyBean.Application.Ads.Comparison;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Our ads against the competitor's, one row per our product, competitor product and platform, with our catalog price
// when we do not advertise it, and the gap against the competitor.
public class OurAdsComparisonRowsTests
{
    private static readonly Guid OurProduct = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TheirProduct = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Competitor = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private static readonly MatchedPairInput Pair = new(
        OurProduct, "Lavazza IL Mattino 250g", 300m,
        Competitor, "Strong Coffee",
        TheirProduct, "Lavazza IL Mattino 250g (store)", 275m);

    [Fact]
    public void OneRowPerPlatform_WhenBothPlatformsAdvertise()
    {
        var ours = new[]
        {
            new OwnAdInput(Guid.NewGuid(), OurProduct, "Lavazza IL Mattino 250g", "Lavazza IL Mattino 250g 300 EGP", null, null, null, AdPlatform.Meta),
            new OwnAdInput(Guid.NewGuid(), OurProduct, "Lavazza IL Mattino 250g", "Lavazza IL Mattino 250g 300 EGP", null, null, null, AdPlatform.Google),
        };
        var theirs = new[]
        {
            new CompetitorAdInput(Guid.NewGuid(), Competitor, "Strong Coffee", TheirProduct, "Lavazza IL Mattino 250g 275 EGP", null, null, null, AdPlatform.Meta),
            new CompetitorAdInput(Guid.NewGuid(), Competitor, "Strong Coffee", TheirProduct, "Lavazza IL Mattino 250g 275 EGP", null, null, null, AdPlatform.Google),
        };

        var rows = OurAdsComparison.Build(ours, theirs, [Pair]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Google", "Meta" }, rows.Select(r => r.Platform).OrderBy(p => p).ToArray());
        Assert.All(rows, r => Assert.Equal(OwnAdComparisonKind.Both, r.Kind));
    }

    [Fact]
    public void CatalogPrice_ShowsWhenWeDoNotAdvertise_AndTheGapIsComputed()
    {
        var theirs = new[]
        {
            new CompetitorAdInput(Guid.NewGuid(), Competitor, "Strong Coffee", TheirProduct, "Lavazza IL Mattino 250g 275 EGP", null, null, null, AdPlatform.Meta),
        };

        var row = Assert.Single(OurAdsComparison.Build([], theirs, [Pair]));

        Assert.Equal(OwnAdComparisonKind.CompetitorOnly, row.Kind);
        Assert.Equal(0, row.OurAdCount);
        Assert.Equal(300m, row.OurPrice);
        Assert.Equal(275m, row.CompetitorPrice);
        Assert.Equal(9.1m, row.GapPercent);
        Assert.Equal("Meta", row.Platform);
    }

    [Fact]
    public void BundleAd_ComparesThePricePerUnit()
    {
        var ours = new[]
        {
            new OwnAdInput(Guid.NewGuid(), OurProduct, "Lavazza IL Mattino 250g", "أي علبتين بـ 490", null, null, null, AdPlatform.Meta),
        };
        var theirs = new[]
        {
            new CompetitorAdInput(Guid.NewGuid(), Competitor, "Strong Coffee", TheirProduct, "Lavazza IL Mattino 250g", null, null, null, AdPlatform.Meta),
        };

        var row = Assert.Single(OurAdsComparison.Build(ours, theirs, [Pair]));

        // 490 for two is 245 each, against the store's 275.
        Assert.Equal(245m, row.OurPrice);
        Assert.Equal(-10.9m, row.GapPercent);
        Assert.False(row.OurAdIsMoreExpensive);
    }

    [Fact]
    public void ProductWithoutACompetitorMatch_IsStillShownOnItsPlatform()
    {
        var ours = new[]
        {
            new OwnAdInput(Guid.NewGuid(), OurProduct, "Lavazza IL Mattino 250g", "Lavazza IL Mattino 250g 300 EGP", null, null, null, AdPlatform.Google),
        };

        var row = Assert.Single(OurAdsComparison.Build(ours, [], []));

        Assert.Equal(OwnAdComparisonKind.OursOnly, row.Kind);
        Assert.Equal("Google", row.Platform);
        Assert.Equal(300m, row.OurPrice);
    }
}
