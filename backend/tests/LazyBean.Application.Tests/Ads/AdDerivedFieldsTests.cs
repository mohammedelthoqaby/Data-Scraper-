using LazyBean.Application.Ads.Common;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The link, the link reason and the Google format an ad gets from its own fields and the catalog.
public class AdDerivedFieldsTests
{
    private const string ImagePreview = "https://tpc.googlesyndication.com/archive/simgad/123";
    private const string VideoPreview = "https://displayads-formats.googleusercontent.com/ads/preview/content.js?creativeId=1";

    [Fact]
    public void GoogleAd_TitleIsTheAdvertiserName_IsNotUsedToLinkAProduct()
    {
        // The advertiser "Lazy Bean" has a product called "Lazy Bean". The advertiser's name is not the ad's copy.
        var ad = GoogleAd(title: "Lazy Bean");

        AdDerivedFields.Apply(ad, [Product("Lazy Bean")]);

        Assert.Null(ad.ProductId);
        // Its copy has not been read yet, so the reason is pending rather than "no product info".
        Assert.Equal(AdLinkReasons.Pending, ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_WithNoCopyAndNoLandingLink_AfterItsDetail_IsNoProductInfo()
    {
        var ad = GoogleAd(title: null);
        ad.DetailFetchedAt = DateTimeOffset.UtcNow;

        AdDerivedFields.Apply(ad, []);

        Assert.Equal(AdLinkReasons.NoProductInfo, ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_HeadlineFromTheDetailNamesAProduct_IsLinkedByTitle()
    {
        var productId = Guid.NewGuid();
        var ad = GoogleAd(title: "Lazy Bean");
        ad.Headline = "Lazy Bean";
        ad.DetailFetchedAt = DateTimeOffset.UtcNow;

        AdDerivedFields.Apply(ad, [new LinkableProduct(productId, "Lazy Bean", null, null)]);

        Assert.Equal(productId, ad.ProductId);
        Assert.Equal("title", ad.LinkMethod);
        Assert.Null(ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_DetailLandingIsTheHomepage_IsLinksToHomepage()
    {
        var ad = GoogleAd(title: null);
        ad.LinkUrl = "https://strongcoffee-eg.com/";
        ad.DetailFetchedAt = DateTimeOffset.UtcNow;

        AdDerivedFields.Apply(ad, [Product("Lazy Bean")]);

        Assert.Equal(AdLinkReasons.LinksToHomepage, ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_UnknownFormat_IsReadFromThePreview()
    {
        var image = GoogleAd(preview: ImagePreview);
        var video = GoogleAd(preview: VideoPreview);
        var text = GoogleAd(preview: null);

        AdDerivedFields.Apply(image, []);
        AdDerivedFields.Apply(video, []);
        AdDerivedFields.Apply(text, []);

        Assert.Equal("Image", image.CreativeFormat);
        Assert.Equal("Video", video.CreativeFormat);
        Assert.Equal("Text", text.CreativeFormat);
    }

    [Fact]
    public void GoogleAd_AKnownFormatIsKept()
    {
        var ad = GoogleAd(preview: ImagePreview);
        ad.CreativeFormat = "Video";

        AdDerivedFields.Apply(ad, []);

        Assert.Equal("Video", ad.CreativeFormat);
    }

    [Fact]
    public void MetaAd_LandingLinkToTheHomepage_IsLinksToHomepage()
    {
        var ad = MetaAd(title: "Espresso coffee", link: "https://strongcoffee-eg.com/");

        AdDerivedFields.Apply(ad, [Product("Lazy Bean")]);

        Assert.Null(ad.ProductId);
        Assert.Equal(AdLinkReasons.LinksToHomepage, ad.LinkReason);
        Assert.Null(ad.CreativeFormat);
    }

    [Fact]
    public void MetaAd_LandingLinkNamesTheProduct_IsLinkedByUrlWithNoReason()
    {
        var productId = Guid.NewGuid();
        var catalog = new[] { new LinkableProduct(productId, "Brasile 250g", null, "https://strongcoffee-eg.com/products/brasile-250g") };
        var ad = MetaAd(title: null, link: "https://strongcoffee-eg.com/products/brasile-250g");

        AdDerivedFields.Apply(ad, catalog);

        Assert.Equal(productId, ad.ProductId);
        Assert.Equal("url", ad.LinkMethod);
        Assert.Null(ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_AiReadNamesAProductExactly_IsLinkedAsAiRead()
    {
        var productId = Guid.NewGuid();
        var ad = GoogleAd(preview: ImagePreview);
        ad.AiReadName = "Lazy Bean";
        ad.AiReadAt = DateTimeOffset.UtcNow;

        AdDerivedFields.Apply(ad, [new LinkableProduct(productId, "Lazy Bean", null, null)]);

        Assert.Equal(productId, ad.ProductId);
        Assert.Equal("ai-read", ad.LinkMethod);
        Assert.Null(ad.LinkReason);
    }

    [Fact]
    public void GoogleAd_AiReadNamesAProductNotInTheCatalog_IsNoMatch()
    {
        var ad = GoogleAd(preview: ImagePreview);
        ad.AiReadName = "Espresso Gold";
        ad.AiReadAt = DateTimeOffset.UtcNow;

        AdDerivedFields.Apply(ad, [Product("Lazy Bean")]);

        Assert.Null(ad.ProductId);
        Assert.Equal(AdLinkReasons.NoMatch, ad.LinkReason);
    }

    [Fact]
    public void CurrentVersion_IsFive_SoTheBackfillRecomputesTheReasons()
    {
        Assert.Equal(5, AdDerivedFields.CurrentVersion);
    }

    private static LinkableProduct Product(string title, string? url = null) =>
        new(Guid.NewGuid(), title, null, url);

    private static Ad GoogleAd(string? title = null, string? preview = null) => new()
    {
        Platform = AdPlatform.Google,
        ExternalAdId = "CR" + Guid.NewGuid().ToString("N")[..10],
        Title = title,
        PreviewUrl = preview,
        Status = AdStatus.Active,
    };

    private static Ad MetaAd(string? title, string? link) => new()
    {
        Platform = AdPlatform.Meta,
        ExternalAdId = "META" + Guid.NewGuid().ToString("N")[..8],
        Title = title,
        LinkUrl = link,
        Status = AdStatus.Active,
    };
}
