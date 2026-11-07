using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Why an ad is not linked, and the label the ads page shows for each reason.
public class AdLinkReasonsTests
{
    [Fact]
    public void Classify_NoTextAndNoLandingLink_IsNoProductInfo()
    {
        Assert.Equal(AdLinkReasons.NoProductInfo, AdLinkReasons.Classify(null, null, null, hasAiName: false));
    }

    [Fact]
    public void Classify_LandingLinkToTheHomepage_IsLinksToHomepage()
    {
        Assert.Equal(
            AdLinkReasons.LinksToHomepage,
            AdLinkReasons.Classify("https://strongcoffee-eg.com/", "Espresso coffee", null, hasAiName: false));
    }

    [Fact]
    public void Classify_LandingLinkToACollection_IsLinksToHomepage()
    {
        Assert.Equal(
            AdLinkReasons.LinksToHomepage,
            AdLinkReasons.Classify("https://strongcoffee-eg.com/collections/capsules", null, null, hasAiName: false));
    }

    [Fact]
    public void Classify_ProductPageWithNoMatchingProduct_IsNoMatch()
    {
        Assert.Equal(
            AdLinkReasons.NoMatch,
            AdLinkReasons.Classify("https://strongcoffee-eg.com/products/brasile-250g", null, null, hasAiName: false));
    }

    [Fact]
    public void Classify_TextButNoLandingLink_IsNoMatch()
    {
        Assert.Equal(AdLinkReasons.NoMatch, AdLinkReasons.Classify(null, "Espresso 250g", null, hasAiName: false));
    }

    [Fact]
    public void Classify_AiReadNamedAProductOutsideTheCatalog_IsNoMatch()
    {
        Assert.Equal(AdLinkReasons.NoMatch, AdLinkReasons.Classify(null, null, null, hasAiName: true));
    }

    [Fact]
    public void Label_NoProductInfo_SaysSo()
    {
        Assert.Equal("No product info in this ad", AdLinkReasons.Label(AdLinkReasons.NoProductInfo, "Strong Coffee"));
    }

    [Fact]
    public void Label_LinksToHomepage_SaysSo()
    {
        Assert.Equal("Links to homepage/collection", AdLinkReasons.Label(AdLinkReasons.LinksToHomepage, "Strong Coffee"));
    }

    [Fact]
    public void Label_NoMatch_NamesTheCompetitor()
    {
        Assert.Equal("No matching product in Strong Coffee catalog", AdLinkReasons.Label(AdLinkReasons.NoMatch, "Strong Coffee"));
    }

    [Fact]
    public void Label_NoMatch_WithoutACompetitorName_SaysCompetitor()
    {
        Assert.Equal("No matching product in competitor catalog", AdLinkReasons.Label(AdLinkReasons.NoMatch, "  "));
    }

    [Fact]
    public void Label_ALinkedAd_HasNoLabel()
    {
        Assert.Null(AdLinkReasons.Label(null, "Strong Coffee"));
    }

    [Fact]
    public void Label_AReasonNotWorkedOutYet_ReadsAsPending()
    {
        Assert.Equal("Link not checked yet", AdLinkReasons.Label(AdLinkReasons.Pending, "Strong Coffee"));
    }
}
