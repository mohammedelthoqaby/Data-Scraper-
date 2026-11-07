using LazyBean.Domain.Entities;

namespace LazyBean.Application.Ads.Common;

// The rules for the images the desktop app uploads for ads: how many, how large, how wide, and when an ad needs them again.
public static class AdThumbnailRules
{
    public const int MaxImagesPerAd = 5;

    // The desktop app targets under 60 KB per image. The server allows a little more, so a close encode is not refused.
    public const int MaxImageBytes = 96 * 1024;
    public const int MaxWidth = 480;
    public const int MaxHeight = 1200;
    public const int MaxItemsPerRequest = 50;

    // An ad not seen for this many days loses its images. Its row comes back if the ad is seen again.
    public const int RetentionDays = 90;

    // The address without its query string. A signed image URL changes between runs while it points at the same image.
    public static string? SourceKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        // A display-format preview is a script that draws the creative, not an image: it has no thumbnail.
        if (uri.Host.EndsWith("displayads-formats.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath}";
    }

    // An ad needs its images when it has none, or when its source image changed. An ad with no image URL needs none.
    public static bool NeedsThumbnail(Ad ad, string? imageUrl)
    {
        var key = SourceKey(imageUrl);
        if (key is null)
        {
            return false;
        }

        return ad.ThumbnailCount == 0 || !string.Equals(ad.ThumbnailSourceKey, key, StringComparison.Ordinal);
    }

    // Null when the image is acceptable, otherwise the reason it is not.
    public static string? Reject(byte[] image)
    {
        if (image.Length == 0)
        {
            return "The image is empty.";
        }

        if (image.Length > MaxImageBytes)
        {
            return $"The image is larger than {MaxImageBytes / 1024} KB.";
        }

        if (!JpegDimensions.TryRead(image, out var width, out var height))
        {
            return "The image is not a JPEG.";
        }

        if (width > MaxWidth)
        {
            return $"The image is wider than {MaxWidth} px.";
        }

        if (height > MaxHeight)
        {
            return $"The image is taller than {MaxHeight} px.";
        }

        return null;
    }
}
