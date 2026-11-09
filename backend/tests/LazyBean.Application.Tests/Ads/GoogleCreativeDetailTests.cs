using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The detail of a Google creative: parsed from real GetCreativeById responses (Fixtures/GoogleCreativeDetail), stored on the ad,
// and used to link the ad again.
public class GoogleCreativeDetailTests
{
    private const string TextCreative = "CR11098911077365186561";
    private const string ImageCreative = "CR02486099634120818689";
    private const string VideoCreative = "CR11813136650899292161";

    [Fact]
    public void TextCreative_ParsesItsVariationsPreviewAndLastShownTime()
    {
        var detail = GoogleCreativeDetailParser.Parse(Fixture("text-format1.json"), TextCreative);

        Assert.NotNull(detail);
        Assert.Equal(TextCreative, detail!.ExternalAdId);
        Assert.Equal(4, detail.Variations.Count);
        Assert.Equal("https://tpc.googlesyndication.com/archive/simgad/12684947687542842870", detail.PreviewUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793950484), detail.LastShownAt);
    }

    [Fact]
    public void ImageCreative_HasNoImageVariation_SoItsPreviewIsTheDisplayPreview()
    {
        var detail = GoogleCreativeDetailParser.Parse(Fixture("image-format2.json"), ImageCreative);

        Assert.NotNull(detail);
        Assert.Equal(3, detail!.Variations.Count);
        Assert.StartsWith("https://displayads-formats.googleusercontent.com/ads/preview/content.js", detail.PreviewUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787671927), detail.LastShownAt);
    }

    [Fact]
    public void VideoCreative_KeepsItsDisplayPreviewVariations()
    {
        var detail = GoogleCreativeDetailParser.Parse(Fixture("video-format3.json"), VideoCreative);

        Assert.NotNull(detail);
        Assert.Equal(3, detail!.Variations.Count);
        Assert.All(detail.Variations, v => Assert.StartsWith("https://displayads-formats.googleusercontent.com/", v));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785162131), detail.LastShownAt);
    }

    [Fact]
    public void ACopyAndALandingLink_AreNotInTheObservedResponses()
    {
        // The real responses carry no headline, description or landing link: they stay empty rather than guessed.
        foreach (var (file, id) in new[] { ("text-format1.json", TextCreative), ("image-format2.json", ImageCreative), ("video-format3.json", VideoCreative) })
        {
            var detail = GoogleCreativeDetailParser.Parse(Fixture(file), id);

            Assert.NotNull(detail);
            Assert.Null(detail!.Headline);
            Assert.Null(detail.Description);
            Assert.Null(detail.LandingUrl);
        }
    }

    [Fact]
    public void AResponseWithNoCreative_IsNull()
    {
        // The request for a creative that is not under this advertiser returns "{}".
        Assert.Null(GoogleCreativeDetailParser.Parse(Fixture("not-found.json"), "CR10852876727672111105"));
    }

    [Fact]
    public void AResponseForAnotherCreative_IsNull()
    {
        Assert.Null(GoogleCreativeDetailParser.Parse(Fixture("text-format1.json"), ImageCreative));
    }

    [Fact]
    public void MalformedJson_IsNull()
    {
        Assert.Null(GoogleCreativeDetailParser.Parse("<html>not json</html>", TextCreative));
    }

    [Fact]
    public void AnHttpsLinkOutsideGoogle_IsTheLandingUrl_AndGoogleLinksAreNot()
    {
        const string json = """
            {"1":{"2":"CR900","4":{"1":"1787671927"},"5":[{"1":{"4":"https://displayads-formats.googleusercontent.com/ads/preview/content.js?x=1"}},{"9":{"1":"https://www.strongcoffee-eg.com/products/brasile-250g"}}]}}
            """;

        var detail = GoogleCreativeDetailParser.Parse(json, "CR900");

        Assert.NotNull(detail);
        Assert.Equal("https://www.strongcoffee-eg.com/products/brasile-250g", detail!.LandingUrl);
    }

    [Fact]
    public void AnUnknownVariationType_IsSkipped_AndDoesNotFailTheParse()
    {
        const string json = """{"1":{"2":"CR901","5":[{"7":{"x":[1,2,3]}},"not an object",{"3":{"2":5}}]}}""";

        var detail = GoogleCreativeDetailParser.Parse(json, "CR901");

        Assert.NotNull(detail);
        Assert.Empty(detail!.Variations);
        Assert.Null(detail.PreviewUrl);
    }

    [Fact]
    public void Applier_StoresTheDetail_AndLinksTheAdFromItsLandingLink()
    {
        var productId = Guid.NewGuid();
        var catalog = new[] { new LinkableProduct(productId, "Brasile 250g", null, "https://www.strongcoffee-eg.com/products/brasile-250g") };
        var ad = GoogleAd("CR901", lastSeen: Fixed);
        var detail = new GoogleCreativeDetailDto(
            "CR901",
            Headline: "Brasile 250g",
            Description: "Smooth coffee",
            LandingUrl: "https://www.strongcoffee-eg.com/products/brasile-250g",
            PreviewUrl: "https://tpc.googlesyndication.com/archive/simgad/1",
            Variations: ["https://tpc.googlesyndication.com/archive/simgad/1"],
            LastShownAt: Fixed.AddDays(-1));

        GoogleCreativeDetailApplier.Apply(ad, detail, Fixed, catalog);

        Assert.Equal("Brasile 250g", ad.Headline);
        Assert.Equal("Smooth coffee", ad.Description);
        Assert.Equal("https://www.strongcoffee-eg.com/products/brasile-250g", ad.LinkUrl);
        Assert.Equal("https://tpc.googlesyndication.com/archive/simgad/1", ad.PreviewUrl);
        Assert.Equal(Fixed, ad.DetailFetchedAt);
        Assert.Equal(Fixed.AddDays(-1), ad.DetailLastShownAt);
        Assert.Equal(productId, ad.ProductId);
        Assert.Equal("url", ad.LinkMethod);
        Assert.Null(ad.LinkReason);
    }

    [Fact]
    public void Applier_AnExistingPreview_IsKept()
    {
        var ad = GoogleAd("CR902", lastSeen: Fixed);
        ad.PreviewUrl = "https://tpc.googlesyndication.com/archive/simgad/listing";
        var detail = new GoogleCreativeDetailDto("CR902", null, null, null, "https://tpc.googlesyndication.com/archive/simgad/detail", [], null);

        GoogleCreativeDetailApplier.Apply(ad, detail, Fixed, []);

        Assert.Equal("https://tpc.googlesyndication.com/archive/simgad/listing", ad.PreviewUrl);
    }

    [Fact]
    public void Applier_AnOverlongHeadline_IsCut()
    {
        var ad = GoogleAd("CR903", lastSeen: Fixed);
        var detail = new GoogleCreativeDetailDto("CR903", new string('x', 900), null, null, null, [], null);

        GoogleCreativeDetailApplier.Apply(ad, detail, Fixed, []);

        Assert.Equal(GoogleCreativeDetailApplier.MaxHeadlineLength, ad.Headline!.Length);
    }

    [Fact]
    public void NeedsDetail_ForANeverReadCreative_IsTrue_AndFalseOnceReadAndNotShownAgain()
    {
        var ad = GoogleAd("CR904", lastSeen: Fixed);
        Assert.True(GoogleCreativeDetailApplier.NeedsDetail(ad));

        GoogleCreativeDetailApplier.Apply(ad, new GoogleCreativeDetailDto("CR904", null, null, null, null, [], Fixed), Fixed, []);
        Assert.False(GoogleCreativeDetailApplier.NeedsDetail(ad));
    }

    [Fact]
    public void NeedsDetail_WhenTheListingShowsTheCreativeAgain_IsTrue()
    {
        var ad = GoogleAd("CR905", lastSeen: Fixed);
        GoogleCreativeDetailApplier.Apply(ad, new GoogleCreativeDetailDto("CR905", null, null, null, null, [], Fixed), Fixed, []);

        ad.LastSeenAt = Fixed.AddDays(3);

        Assert.True(GoogleCreativeDetailApplier.NeedsDetail(ad));
    }

    [Fact]
    public void NeedsDetail_IsOnlyForGoogleAds()
    {
        var meta = new Ad { Platform = AdPlatform.Meta, ExternalAdId = "M1", LastSeenAt = Fixed };

        Assert.False(GoogleCreativeDetailApplier.NeedsDetail(meta));
    }

    private static readonly DateTimeOffset Fixed = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static Ad GoogleAd(string externalId, DateTimeOffset lastSeen) => new()
    {
        Platform = AdPlatform.Google,
        ExternalAdId = externalId,
        LastSeenAt = lastSeen,
        FirstSeenAt = lastSeen,
        Status = AdStatus.Active,
        Title = "Advertiser name",
    };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GoogleCreativeDetail", name));
}
