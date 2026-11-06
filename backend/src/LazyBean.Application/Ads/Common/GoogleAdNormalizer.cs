using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Ads.Common;

// Makes one Google creative safe to store before it reaches the database. The limits match the ads table
// columns. Text loses the characters PostgreSQL refuses (NUL and unpaired surrogates), and raw JSON must parse
// and must not hold escapes that jsonb rejects. A creative that cannot be stored is skipped with a reason; a
// creative whose text or links are too long keeps its identity and has the field trimmed or dropped.
public static class GoogleAdNormalizer
{
    public const int MaxExternalAdIdLength = 200;
    public const int MaxTitleLength = 500;
    public const int MaxBodyLength = 4000;
    public const int MaxUrlLength = 2000;

    private static readonly DateTimeOffset EarliestPlausibleDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Regex NulEscape = new(@"\\u0000", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A high surrogate escape with no low surrogate escape after it, or a low surrogate escape with no high one before it.
    private static readonly Regex UnpairedSurrogateEscape = new(
        @"\\u[dD][89abAB][0-9a-fA-F]{2}(?!\\u[dD][c-fC-F][0-9a-fA-F]{2})|(?<!\\u[dD][89abAB][0-9a-fA-F]{2})\\u[dD][c-fC-F][0-9a-fA-F]{2}",
        RegexOptions.Compiled);

    public static GoogleAdNormalizationResult Normalize(GoogleAdPayloadItem? item, DateTimeOffset now)
    {
        if (item is null)
        {
            return GoogleAdNormalizationResult.Rejected(null, "The creative was empty.");
        }

        var externalId = Clean(item.ExternalAdId);
        if (externalId is null)
        {
            return GoogleAdNormalizationResult.Rejected(null, "The creative has no ID.");
        }

        if (externalId.Length > MaxExternalAdIdLength)
        {
            return GoogleAdNormalizationResult.Rejected(
                Truncate(externalId, 40),
                $"The creative ID is longer than {MaxExternalAdIdLength} characters.");
        }

        return GoogleAdNormalizationResult.Accepted(new GoogleAdNormalized(
            externalId,
            Truncate(Clean(item.Title), MaxTitleLength),
            Truncate(Clean(item.Body), MaxBodyLength),
            LinkOrNull(item.LinkUrl),
            LinkOrNull(item.SnapshotUrl),
            ClampDate(item.FirstSeen, now),
            ClampDate(item.LastSeen, now),
            CleanRawJson(item.RawJson),
            LinkOrNull(item.PreviewUrl),
            FormatName(item.Format)));
    }

    // Returns the raw JSON only when it parses and jsonb will accept it; anything else is stored as null.
    public static string? CleanRawJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Contains('\0') || HasUnpairedSurrogate(raw) ||
            NulEscape.IsMatch(raw) || UnpairedSurrogateEscape.IsMatch(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return raw;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Removes NUL characters and unpaired surrogates, trims, and returns null when nothing is left.
    public static string? Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                continue;
            }

            builder.Append(c);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    // Cuts to maxLength without leaving half of a surrogate pair at the end.
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        var end = maxLength;
        if (char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return value[..end];
    }

    // The creative format as shown in the Ads page badge: Text, Image, Video or Display.
    private static string? FormatName(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "text" => "Text",
        "image" => "Image",
        "video" => "Video",
        _ => "Display",
    };

    private static string? LinkOrNull(string? value)
    {
        var cleaned = Clean(value);
        return cleaned is null || cleaned.Length > MaxUrlLength ? null : cleaned;
    }

    private static DateTimeOffset? ClampDate(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return null;
        }

        var utc = value.Value.ToUniversalTime();
        return utc < EarliestPlausibleDate || utc > now.AddDays(1) ? null : utc;
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }
}

// A creative that passed the checks, ready to be stored.
public sealed record GoogleAdNormalized(
    string ExternalAdId,
    string? Title,
    string? Body,
    string? LinkUrl,
    string? SnapshotUrl,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    string? RawJson,
    string? PreviewUrl,
    string? CreativeFormat);

// Either an accepted creative or the reason it was skipped. ExternalAdId is set when the creative had one.
public sealed record GoogleAdNormalizationResult(string? ExternalAdId, GoogleAdNormalized? Ad, string? SkipReason)
{
    public static GoogleAdNormalizationResult Accepted(GoogleAdNormalized ad) => new(ad.ExternalAdId, ad, null);

    public static GoogleAdNormalizationResult Rejected(string? externalAdId, string reason) => new(externalAdId, null, reason);
}
