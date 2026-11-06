using System.Text;
using System.Text.RegularExpressions;

namespace LazyBean.Application.Ads.Common;

public sealed record LinkableProduct(Guid Id, string Title, string? Handle, string? CanonicalUrl);

public sealed record AdLinkInput(string? LinkUrl, string? Title, string? Body);

// Method is "handle", "url", "title" or "none". A "none" means the ad is shown as "Not linked".
public sealed record AdProductLink(Guid? ProductId, string Method)
{
    public static AdProductLink NotLinked { get; } = new(null, "none");
}

// Links a competitor ad to the product it advertises, and never to a product it does not clearly name:
//   1. The landing link names a product (/products/{handle}) and exactly one product has that handle.
//   2. The landing link is a specific product page and exactly one product has that canonical URL.
//   3. The full name (two or more words) of exactly one product is written in the ad. A tie is not linked.
// Anything weaker is "Not linked", so an ad never shows a wrong product.
public static class AdProductLinker
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "عرض", "خاص", "جنيه", "جنية", "egp", "le", "for", "with", "and", "the", "of", "only",
        "offer", "sale", "from", "new", "بـ", "ب", "من", "في", "على", "و",
    };

    public static AdProductLink Link(AdLinkInput ad, IReadOnlyList<LinkableProduct> catalog)
    {
        if (catalog.Count == 0)
        {
            return AdProductLink.NotLinked;
        }

        // 1. The handle in the landing link.
        var handle = CreativeHashHelper.ExtractProductHandle(ad.LinkUrl);
        if (!string.IsNullOrWhiteSpace(handle))
        {
            var byHandle = catalog.Where(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byHandle.Count == 1)
            {
                return new AdProductLink(byHandle[0].Id, "handle");
            }
        }

        // 2. The landing link is the canonical URL of exactly one product, and it is a product page.
        var normalizedLink = CreativeHashHelper.NormalizeProductUrl(ad.LinkUrl);
        if (IsSpecificPage(normalizedLink))
        {
            var byUrl = catalog
                .Where(p => !string.IsNullOrWhiteSpace(p.CanonicalUrl) &&
                            string.Equals(CreativeHashHelper.NormalizeProductUrl(p.CanonicalUrl), normalizedLink, StringComparison.Ordinal))
                .ToList();
            if (byUrl.Count == 1)
            {
                return new AdProductLink(byUrl[0].Id, "url");
            }
        }

        // 3. The name of one product is fully written in the ad text. The longest such name wins; a tie is not linked.
        var adWords = Words(ad.Title + " " + ad.Body);
        if (adWords.Count == 0)
        {
            return AdProductLink.NotLinked;
        }

        var named = new List<(LinkableProduct Product, int Length)>();
        foreach (var product in catalog)
        {
            var nameWords = Words(product.Title);
            if (nameWords.Count >= 2 && nameWords.All(adWords.Contains))
            {
                named.Add((product, nameWords.Count));
            }
        }

        if (named.Count == 0)
        {
            return AdProductLink.NotLinked;
        }

        var longest = named.Max(n => n.Length);
        var best = named.Where(n => n.Length == longest).ToList();
        return best.Count == 1 ? new AdProductLink(best[0].Product.Id, "title") : AdProductLink.NotLinked;
    }

    // A product page has a path: the store's root and collection pages are not a product.
    private static bool IsSpecificPage(string normalizedUrl)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 1 &&
               !segments.Any(s => s is "collections" or "category" or "categories" or "shop" or "search");
    }

    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }

        foreach (var word in Regex.Split(Normalize(text), @"[^\p{L}\p{N}]+"))
        {
            if (word.Length > 0 && !StopWords.Contains(word))
            {
                words.Add(word);
            }
        }

        return words;
    }

    // Lower case, Arabic letter forms unified, tatweel and diacritics removed.
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            switch (ch)
            {
                case 'أ':
                case 'إ':
                case 'آ':
                    builder.Append('ا');
                    break;
                case 'ى':
                    builder.Append('ي');
                    break;
                case 'ة':
                    builder.Append('ه');
                    break;
                case 'ـ':
                    break;
                default:
                    if (!(ch >= 'ً' && ch <= 'ٟ') && ch != 'ٰ')
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
