using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// A Google creative is Text, Image or Video. The listing says which, and the preview decides when it does not.
public class GoogleCreativeFormatTests
{
    private const string ImagePreview = "https://tpc.googlesyndication.com/archive/simgad/123";
    private const string VideoPreview = "https://displayads-formats.googleusercontent.com/ads/preview/content.js?creativeId=1";

    [Theory]
    [InlineData("Text", "Text")]
    [InlineData("Image", "Image")]
    [InlineData("Video", "Video")]
    public void Resolve_AKnownFormatIsKept(string current, string expected)
    {
        Assert.Equal(expected, GoogleCreativeFormat.Resolve(current, ImagePreview));
    }

    [Fact]
    public void Resolve_UnknownFormatWithAFormatPreview_IsVideo()
    {
        Assert.Equal("Video", GoogleCreativeFormat.Resolve(null, VideoPreview));
    }

    [Fact]
    public void Resolve_UnknownFormatWithAnImagePreview_IsImage()
    {
        Assert.Equal("Image", GoogleCreativeFormat.Resolve(null, ImagePreview));
    }

    [Fact]
    public void Resolve_UnknownFormatWithNoPreview_IsText()
    {
        Assert.Equal("Text", GoogleCreativeFormat.Resolve(null, null));
        Assert.Equal("Text", GoogleCreativeFormat.Resolve("Display", "  "));
    }

    [Fact]
    public void Normalize_TheListingFormatWins()
    {
        var result = GoogleAdNormalizer.Normalize(Item(format: "image", preview: VideoPreview), DateTimeOffset.UtcNow);

        Assert.Equal("Image", result.Ad!.CreativeFormat);
    }

    [Fact]
    public void Normalize_AnUnknownListingFormatIsReadFromThePreview()
    {
        var result = GoogleAdNormalizer.Normalize(Item(format: "display", preview: VideoPreview), DateTimeOffset.UtcNow);

        Assert.Equal("Video", result.Ad!.CreativeFormat);
    }

    [Fact]
    public void Normalize_NeverGivesTheGenericCreativeFormat()
    {
        var result = GoogleAdNormalizer.Normalize(Item(format: null, preview: null), DateTimeOffset.UtcNow);

        Assert.NotEqual("Creative", result.Ad!.CreativeFormat);
        Assert.Equal("Text", result.Ad.CreativeFormat);
    }

    private static GoogleAdPayloadItem Item(string? format, string? preview) => new(
        "CR15385076473547194369",
        format,
        "Advertiser name",
        null,
        null,
        null,
        null,
        null,
        null,
        preview);
}
