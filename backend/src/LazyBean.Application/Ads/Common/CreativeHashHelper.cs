using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LazyBean.Application.Ads.Common;

public static partial class CreativeHashHelper
{
    private static readonly Regex UrlRegex = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HashtagRegex = new(@"#\S+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes ad body for campaign grouping: lowercase, strip URLs, emojis, hashtags, extra whitespace.
    /// </summary>
    public static string NormalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var text = body;

        // 1. Strip URLs
        text = UrlRegex.Replace(text, " ");

        // 2. Strip hashtags
        text = HashtagRegex.Replace(text, " ");

        // 3. Strip emojis & symbols (surrogates, dingbats, miscellaneous symbols, symbol categories)
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsSurrogate(ch))
            {
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }
                continue;
            }

            var cat = char.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol)
            {
                continue;
            }

            // Common emoji / dingbats / misc symbols in BMP
            if (ch >= 0x2600 && ch <= 0x27BF)
            {
                continue;
            }

            sb.Append(ch);
        }
        text = sb.ToString();

        // 4. Lowercase
        text = text.ToLowerInvariant();

        // 5. Collapse extra whitespace
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return text;
    }

    /// <summary>
    /// Computes campaign key hash: identity + normalized body.
    /// </summary>
    public static string Compute(Guid identityId, string? body)
    {
        var norm = NormalizeBody(body);
        var input = $"{identityId:N}|{norm}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Backward-compatible overload when identityId is not provided.
    /// </summary>
    public static string Compute(string? title, string? body, string? snapshotUrl = null)
    {
        var norm = NormalizeBody(body);
        var input = $"{title?.Trim().ToLowerInvariant()}|{norm}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Extracts product handle or permalink from Shopify (/products/{handle}) or WooCommerce (/product/{slug}) URL.
    /// </summary>
    public static string? ExtractProductHandle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + url.TrimStart('/'), UriKind.Absolute, out uri))
            {
                return null;
            }
        }

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path)) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].ToLowerInvariant();
            if ((seg == "products" || seg == "product") && i + 1 < segments.Length)
            {
                return segments[i + 1].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes URL by stripping www., dropping query params and fragments, and converting to lowercase.
    /// </summary>
    public static string NormalizeProductUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + url.TrimStart('/'), UriKind.Absolute, out uri))
            {
                return url.Trim().ToLowerInvariant();
            }
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return $"https://{host}{path}".ToLowerInvariant();
    }
}
