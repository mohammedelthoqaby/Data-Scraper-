using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Ads.Common;

// Reads a GetCreativeById response (LookupService). The fields read are the ones seen on real responses:
//   "1"."2"              the creative ID (checked against the request)
//   "1"."4"."1"          the last-shown time, unix seconds as a string (the same time as the listing's last-seen)
//   "1"."5"[]."3"."2"    an <img src> of an image variation (a Google image host)
//   "1"."5"[]."1"."4"    a display-format preview URL of a video or HTML5 variation (a Google host)
// A response does not carry the headline or the description of a creative. If a response does carry an https link that is
// not on a Google host, that link is the landing link. Anything else is skipped, so an unknown variation type or a new field
// never fails the parse.
public static partial class GoogleCreativeDetailParser
{
    public const int MaxVariations = 10;

    private const int MaxDepth = 24;

    private static readonly string[] GoogleDomains =
    [
        "google.com", "googleapis.com", "googleusercontent.com", "googlesyndication.com", "googleadservices.com",
        "googletagmanager.com", "gstatic.com", "ggpht.com", "youtube.com", "youtu.be", "doubleclick.net",
    ];

    [GeneratedRegex(@"<img[^>]+src=[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSource();

    [GeneratedRegex(@"https://[^\s""'<>\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex HttpsLink();

    // Null when the response holds no creative (for example "{}" for a creative under another advertiser), or when it
    // describes a different creative than the request asked for.
    public static GoogleCreativeDetailDto? Parse(string json, string creativeId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseRoot(doc.RootElement, creativeId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static GoogleCreativeDetailDto? ParseRoot(JsonElement root, string creativeId)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("1", out var detail) ||
            detail.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (detail.TryGetProperty("2", out var idProp) &&
            idProp.ValueKind == JsonValueKind.String &&
            !string.Equals(idProp.GetString(), creativeId, StringComparison.Ordinal))
        {
            return null;
        }

        var imageVariations = new List<string>();
        var previewVariations = new List<string>();
        if (detail.TryGetProperty("5", out var variations) && variations.ValueKind == JsonValueKind.Array)
        {
            foreach (var variation in variations.EnumerateArray())
            {
                if (variation.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var image = ImageVariation(variation);
                if (image is not null)
                {
                    imageVariations.Add(image);
                    continue;
                }

                var preview = PreviewVariation(variation);
                if (preview is not null)
                {
                    previewVariations.Add(preview);
                }
            }
        }

        var urls = imageVariations.Concat(previewVariations).Distinct(StringComparer.Ordinal).Take(MaxVariations).ToList();

        return new GoogleCreativeDetailDto(
            creativeId,
            Headline: null,
            Description: null,
            LandingUrl: LandingUrl(root),
            PreviewUrl: imageVariations.FirstOrDefault() ?? previewVariations.FirstOrDefault(),
            Variations: urls,
            LastShownAt: LastShown(detail));
    }

    // "3"."2" holds an <img> tag; its src is a Google image.
    private static string? ImageVariation(JsonElement variation)
    {
        if (!variation.TryGetProperty("3", out var media) ||
            media.ValueKind != JsonValueKind.Object ||
            !media.TryGetProperty("2", out var html) ||
            html.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var match = ImageSource().Match(html.GetString() ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var src = WebUtility.HtmlDecode(match.Groups["src"].Value);
        return IsGoogleHttps(src) ? src : null;
    }

    // "1"."4" holds a display-format preview document; it is a Google address.
    private static string? PreviewVariation(JsonElement variation)
    {
        if (!variation.TryGetProperty("1", out var format) ||
            format.ValueKind != JsonValueKind.Object ||
            !format.TryGetProperty("4", out var url) ||
            url.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = url.GetString();
        return value is not null && IsGoogleHttps(value) ? value : null;
    }

    private static DateTimeOffset? LastShown(JsonElement detail)
    {
        if (!detail.TryGetProperty("4", out var shown) ||
            shown.ValueKind != JsonValueKind.Object ||
            !shown.TryGetProperty("1", out var seconds) ||
            seconds.ValueKind != JsonValueKind.String ||
            !long.TryParse(seconds.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) ||
            unix <= 0 ||
            unix > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unix);
    }

    // The first https address in the response that is not on a Google domain.
    private static string? LandingUrl(JsonElement root)
    {
        var found = FindLanding(root, 0);
        return found;
    }

    private static string? FindLanding(JsonElement element, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                foreach (Match match in HttpsLink().Matches(element.GetString() ?? string.Empty))
                {
                    if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) && !IsGoogleDomain(uri.Host))
                    {
                        return uri.AbsoluteUri;
                    }
                }

                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindLanding(item, depth + 1);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var found = FindLanding(property.Value, depth + 1);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static bool IsGoogleHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && IsGoogleDomain(uri.Host);

    private static bool IsGoogleDomain(string host)
    {
        var lower = host.ToLowerInvariant();
        return GoogleDomains.Any(domain => lower == domain || lower.EndsWith("." + domain, StringComparison.Ordinal));
    }
}
