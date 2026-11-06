using System.Text.RegularExpressions;

namespace LazyBean.Application.Ads.Common;

// Either a numeric Meta page ID, read straight from the link, or a page name the desktop app must resolve.
public sealed record FacebookPageReference(string? PageId, string? Slug);

// Reads a facebook.com page link. Pure: no network. Returns null when the text is not a Facebook page link.
public static partial class FacebookPageUrl
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook.com", "www.facebook.com", "m.facebook.com", "web.facebook.com", "fb.com", "www.fb.com",
    };

    // First path segments that are Facebook features, not page names.
    private static readonly HashSet<string> ReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "pages", "groups", "events", "watch", "photo", "photos", "photo.php", "ads", "share", "sharer",
        "sharer.php", "login", "people", "story.php", "permalink.php", "reel", "reels", "video", "videos",
        "marketplace", "hashtag", "help", "policies", "plugins", "dialog", "tr", "l.php",
    };

    public static FacebookPageReference? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        // Links are often pasted without the scheme.
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !Hosts.Contains(uri.Host))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);

        // Ad Library links carry the page ID directly.
        if (query.TryGetValue("view_all_page_id", out var viewAll) && IsNumeric(viewAll))
        {
            return new FacebookPageReference(viewAll, null);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var first = segments[0];

        if (first.Equals("profile.php", StringComparison.OrdinalIgnoreCase))
        {
            return query.TryGetValue("id", out var profileId) && IsNumeric(profileId)
                ? new FacebookPageReference(profileId, null)
                : null;
        }

        if (first.Equals("pages", StringComparison.OrdinalIgnoreCase))
        {
            // /pages/<name>/<id> carries the ID; /pages/<name> needs a lookup by name.
            if (segments.Length >= 3 && IsNumeric(segments[2]))
            {
                return new FacebookPageReference(segments[2], null);
            }

            return segments.Length >= 2 && IsSlug(segments[1])
                ? new FacebookPageReference(null, segments[1])
                : null;
        }

        if (IsNumeric(first))
        {
            return new FacebookPageReference(first, null);
        }

        if (ReservedSegments.Contains(first) || !IsSlug(first))
        {
            return null;
        }

        return new FacebookPageReference(null, first);
    }

    private static bool IsNumeric(string value) =>
        value.Length > 0 && value.All(char.IsAsciiDigit);

    private static bool IsSlug(string value) => SlugPattern().IsMatch(value);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            values.TryAdd(key, parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        return values;
    }

    // Facebook page names: letters, digits and periods, 5 to 80 characters in practice; accept 3 and up.
    [GeneratedRegex("^[A-Za-z0-9.]{3,80}$")]
    private static partial Regex SlugPattern();
}
