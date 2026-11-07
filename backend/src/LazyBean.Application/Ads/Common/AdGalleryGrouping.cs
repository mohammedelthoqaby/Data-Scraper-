using System.Security.Cryptography;
using System.Text;

namespace LazyBean.Application.Ads.Common;

// Ads from the same store with the same text and the same first image are one card with several variations.
public static class AdGalleryGrouping
{
    // An ad with no text and no image cannot be matched to another, so it stays a card of its own.
    public static string Key(Guid adId, Guid competitorId, string? headline, string? body, string? imageSha256)
    {
        var text = Normalize(headline) + "\n" + Normalize(body);
        var hasImage = !string.IsNullOrWhiteSpace(imageSha256);
        if (text.Trim().Length == 0 && !hasImage)
        {
            return $"single|{adId:N}";
        }

        var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return $"{competitorId:N}|{textHash}|{imageSha256 ?? string.Empty}";
    }

    // Lower case, one space between words, and no leading or trailing space.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
