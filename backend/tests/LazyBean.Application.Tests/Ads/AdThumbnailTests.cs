using System.Text;
using LazyBean.Application.Ads.Common;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// The image rules: the JPEG frame header, the size and width limits, the source key and when an ad needs its images again,
// and the grouping of identical ads into one card.
public class AdThumbnailTests
{
    [Fact]
    public void JpegDimensions_ReadsTheFrameHeader()
    {
        Assert.True(JpegDimensions.TryRead(Jpeg(800, 600), out var width, out var height));

        Assert.Equal(800, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void JpegDimensions_RefusesWhatIsNotAJpeg()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

        Assert.False(JpegDimensions.TryRead(png, out _, out _));
    }

    [Fact]
    public void Reject_AcceptsASmallJpeg()
    {
        Assert.Null(AdThumbnailRules.Reject(Jpeg(480, 360)));
    }

    [Fact]
    public void Reject_RefusesAnImageWiderThanTheLimit()
    {
        var reason = AdThumbnailRules.Reject(Jpeg(800, 600));

        Assert.Equal($"The image is wider than {AdThumbnailRules.MaxWidth} px.", reason);
    }

    [Fact]
    public void Reject_RefusesAnImageTallerThanTheLimit()
    {
        var reason = AdThumbnailRules.Reject(Jpeg(300, 2000));

        Assert.Equal($"The image is taller than {AdThumbnailRules.MaxHeight} px.", reason);
    }

    [Fact]
    public void Reject_RefusesAnImageLargerThanTheByteLimit()
    {
        var reason = AdThumbnailRules.Reject(Jpeg(480, 360, padding: AdThumbnailRules.MaxImageBytes));

        Assert.Equal($"The image is larger than {AdThumbnailRules.MaxImageBytes / 1024} KB.", reason);
    }

    [Fact]
    public void Reject_RefusesWhatIsNotAJpeg()
    {
        Assert.Equal("The image is not a JPEG.", AdThumbnailRules.Reject(Encoding.ASCII.GetBytes("GIF89a....")));
        Assert.Equal("The image is empty.", AdThumbnailRules.Reject([]));
    }

    [Theory]
    [InlineData("https://tpc.googlesyndication.com/archive/simgad/123?sig=abc&expires=1", "tpc.googlesyndication.com/archive/simgad/123")]
    [InlineData("https://SCONTENT.xx.fbcdn.net/v/t45/image.jpg?oe=1", "scontent.xx.fbcdn.net/v/t45/image.jpg")]
    public void SourceKey_IgnoresTheSignedQueryString(string url, string expected)
    {
        Assert.Equal(expected, AdThumbnailRules.SourceKey(url));
    }

    [Theory]
    [InlineData("http://example.com/a.jpg")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://displayads-formats.googleusercontent.com/ads/preview/content.js?creativeId=1")]
    public void SourceKey_IsNullForAddressesThatAreNotPlainImages(string? url)
    {
        Assert.Null(AdThumbnailRules.SourceKey(url));
    }

    [Fact]
    public void NeedsThumbnail_WhenTheAdHasNoImages_OrItsSourceChanged()
    {
        var ad = new Ad { ThumbnailCount = 0 };
        Assert.True(AdThumbnailRules.NeedsThumbnail(ad, "https://tpc.googlesyndication.com/archive/simgad/1?x=1"));

        ad.ThumbnailCount = 1;
        ad.ThumbnailSourceKey = "tpc.googlesyndication.com/archive/simgad/1";
        Assert.False(AdThumbnailRules.NeedsThumbnail(ad, "https://tpc.googlesyndication.com/archive/simgad/1?x=2"));
        Assert.True(AdThumbnailRules.NeedsThumbnail(ad, "https://tpc.googlesyndication.com/archive/simgad/2?x=2"));
    }

    [Fact]
    public void NeedsThumbnail_IsFalseWhenThereIsNoPlainImage()
    {
        var ad = new Ad { ThumbnailCount = 0 };

        Assert.False(AdThumbnailRules.NeedsThumbnail(ad, null));
        Assert.False(AdThumbnailRules.NeedsThumbnail(ad, "https://displayads-formats.googleusercontent.com/ads/preview/content.js?x=1"));
    }

    [Fact]
    public void Grouping_IdenticalTextAndImage_FromOneStore_IsOneCard()
    {
        var store = Guid.NewGuid();
        var first = AdGalleryGrouping.Key(Guid.NewGuid(), store, "Brasile 250g", "Smooth  coffee", "abc");
        var second = AdGalleryGrouping.Key(Guid.NewGuid(), store, "  BRASILE 250G ", "smooth coffee", "abc");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Grouping_DifferentImage_OrStore_IsADifferentCard()
    {
        var store = Guid.NewGuid();
        var baseKey = AdGalleryGrouping.Key(Guid.NewGuid(), store, "Brasile 250g", null, "abc");

        Assert.NotEqual(baseKey, AdGalleryGrouping.Key(Guid.NewGuid(), store, "Brasile 250g", null, "def"));
        Assert.NotEqual(baseKey, AdGalleryGrouping.Key(Guid.NewGuid(), Guid.NewGuid(), "Brasile 250g", null, "abc"));
    }

    [Fact]
    public void Grouping_AnAdWithNoTextAndNoImage_StaysAloneEvenWhenItsSiblingsAreTheSame()
    {
        var store = Guid.NewGuid();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();

        Assert.NotEqual(AdGalleryGrouping.Key(one, store, null, null, null), AdGalleryGrouping.Key(two, store, null, null, null));
    }

    // A JPEG made of the frame header and padding only: enough for the header and size rules.
    private static byte[] Jpeg(int width, int height, int padding = 0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // APP0 (JFIF) segment with a 14-byte payload.
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        // SOF0 frame header: precision, height, width, three components.
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
            0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);

        bytes.AddRange(new byte[padding]);
        bytes.AddRange([0xFF, 0xD9]);
        return bytes.ToArray();
    }
}
