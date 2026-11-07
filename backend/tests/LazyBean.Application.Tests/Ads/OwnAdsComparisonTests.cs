using LazyBean.Application.Ads.Comparison;
using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Our ads (Lazy Bean) compared with competitor ads for the same matched product, and the Facebook page-link reader.
public class OwnAdsComparisonTests
{
    private static readonly Guid OurProduct = Guid.NewGuid();
    private static readonly Guid TheirProduct = Guid.NewGuid();
    private static readonly Guid CompetitorId = Guid.NewGuid();

    private static OwnAdInput Own(string? title = "Espresso 250g 120 EGP", Guid? product = null) =>
        new(Guid.NewGuid(), product ?? OurProduct, "Espresso 250g", title, null, "https://ads.example/own", null);

    private static CompetitorAdInput Theirs(string? title = "Espresso 250g 100 EGP", Guid? product = null) =>
        new(Guid.NewGuid(), CompetitorId, "Coffee Zone", product ?? TheirProduct, title, null, "https://ads.example/theirs", null);

    private static MatchedPairInput Pair(decimal? ourStore = null, decimal? theirStore = null) =>
        new(OurProduct, "Espresso 250g", ourStore, CompetitorId, "Coffee Zone", TheirProduct, "Espresso 250g (Coffee Zone)", theirStore);

    [Fact]
    public void Both_WhenWeAndTheCompetitorAdvertiseTheSameMatchedProduct_AndOurPriceIsHigher_IsFlagged()
    {
        var rows = OurAdsComparison.Build([Own("Espresso 250g 120 EGP")], [Theirs("Espresso 250g 100 EGP")], [Pair()]);

        var row = Assert.Single(rows);
        Assert.Equal(OwnAdComparisonKind.Both, row.Kind);
        Assert.Equal(120m, row.OurPrice);
        Assert.Equal(100m, row.CompetitorPrice);
        Assert.True(row.OurAdIsMoreExpensive);
        Assert.Equal(1, row.OurAdCount);
        Assert.Equal(1, row.CompetitorAdCount);
    }

    [Fact]
    public void Both_WhenOurPriceIsLower_IsNotFlagged()
    {
        var rows = OurAdsComparison.Build([Own("Espresso 250g 90 EGP")], [Theirs("Espresso 250g 100 EGP")], [Pair()]);

        var row = Assert.Single(rows);
        Assert.Equal(OwnAdComparisonKind.Both, row.Kind);
        Assert.False(row.OurAdIsMoreExpensive);
    }

    [Fact]
    public void Both_IsNotFlagged_WhenEitherPriceIsUnknown()
    {
        var rows = OurAdsComparison.Build([Own("Espresso 250g")], [Theirs("Espresso 250g 100 EGP")], [Pair()]);

        var row = Assert.Single(rows);
        Assert.Null(row.OurPrice);
        Assert.False(row.OurAdIsMoreExpensive);
    }

    [Fact]
    public void CompetitorOnly_IsAnOpportunity_WhenWeDoNotAdvertiseTheMatchedProduct()
    {
        var rows = OurAdsComparison.Build([], [Theirs()], [Pair()]);

        var row = Assert.Single(rows);
        Assert.Equal(OwnAdComparisonKind.CompetitorOnly, row.Kind);
        Assert.Equal(OurProduct, row.OurProductId);
        Assert.Equal(TheirProduct, row.CompetitorProductId);
        Assert.Equal(0, row.OurAdCount);
    }

    [Fact]
    public void StorePrice_IsTheFallback_WhenAnAdStatesNoPrice_AndCanFlagOurPrice()
    {
        var rows = OurAdsComparison.Build([Own("Espresso 250g")], [Theirs("Espresso 250g 100 EGP")], [Pair(ourStore: 150m)]);

        var row = Assert.Single(rows);
        Assert.Equal(150m, row.OurPrice);
        Assert.True(row.OurAdIsMoreExpensive);
    }

    [Fact]
    public void OursOnly_ForOurAdWithNoMatchedCompetitorProduct()
    {
        var rows = OurAdsComparison.Build([Own()], [], []);

        var row = Assert.Single(rows);
        Assert.Equal(OwnAdComparisonKind.OursOnly, row.Kind);
        Assert.Null(row.CompetitorProductId);
        Assert.Null(row.CompetitorName);
    }

    [Fact]
    public void CompetitorAdOnAnUnmatchedProduct_IsNotAnOpportunity()
    {
        var rows = OurAdsComparison.Build([], [Theirs()], []);

        Assert.Empty(rows);
    }

    [Fact]
    public void DuplicatePairs_ProduceOneRow()
    {
        var rows = OurAdsComparison.Build([Own()], [Theirs()], [Pair(), Pair()]);

        Assert.Single(rows);
    }

    [Theory]
    [InlineData("https://www.facebook.com/profile.php?id=61565792458339", "61565792458339", null)]
    [InlineData("https://www.facebook.com/ads/library/?active_status=all&view_all_page_id=433792133147901", "433792133147901", null)]
    [InlineData("https://www.facebook.com/pages/Lazy-Bean/433792133147901", "433792133147901", null)]
    [InlineData("https://www.facebook.com/433792133147901", "433792133147901", null)]
    [InlineData("https://www.facebook.com/lazybeanonline", null, "lazybeanonline")]
    [InlineData("https://www.facebook.com/lazybeanonline/", null, "lazybeanonline")]
    [InlineData("https://m.facebook.com/lazybeanonline?ref=bookmarks", null, "lazybeanonline")]
    [InlineData("facebook.com/lazybeanonline", null, "lazybeanonline")]
    public void Parse_ReadsThePageIdOrNameFromAFacebookLink(string url, string? pageId, string? slug)
    {
        var reference = FacebookPageUrl.Parse(url);

        Assert.NotNull(reference);
        Assert.Equal(pageId, reference.PageId);
        Assert.Equal(slug, reference.Slug);
    }

    [Theory]
    [InlineData("https://evil.example/lazybeanonline")]
    [InlineData("https://facebook.com.evil.example/lazybeanonline")]
    [InlineData("https://www.facebook.com/watch/?v=123")]
    [InlineData("https://www.facebook.com/ab")]
    [InlineData("not a link")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_RejectsWhatIsNotAFacebookPageLink(string? url)
    {
        Assert.Null(FacebookPageUrl.Parse(url));
    }
}
