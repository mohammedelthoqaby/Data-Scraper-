using System.Text.RegularExpressions;
using LazyBean.Application.Collection.Normalization;

namespace LazyBean.Application.Matching;

public static partial class TitleSimilarity
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "coffee", "espresso", "caffe", "cafe", "pack", "packs", "box", "boxes",
        "of", "and", "with", "for", "in", "the", "a", "an", "original", "compatible",
        "قهوة", "اسبريسو", "كافيه", "بن", "مع", "من", "في", "حبوب", "مطحون", "مطحونة",
        "كبسول", "كبسولة", "كبسولات", "علبة", "باكيت", "ظرف", "أكياس", "كيس"
    };

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]+")]
    private static partial Regex NonAlphaNumeric();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static HashSet<string> ExtractTokens(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return [];

        var decoded = TitleNormalizer.Decode(title);
        var ascii = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded);
        var normalized = ArabicTextNormalizer.NormalizeLetters(ascii);
        var cleaned = NonAlphaNumeric().Replace(normalized, " ");
        var collapsed = Whitespace().Replace(cleaned, " ").Trim();

        var tokens = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tokens)
        {
            var lower = t.ToLowerInvariant();
            if (lower.Length >= 2 && !StopWords.Contains(lower))
            {
                result.Add(lower);
            }
        }

        return result;
    }

    public static double TokenDiceSimilarity(HashSet<string> tokens1, HashSet<string> tokens2)
    {
        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 1.0;
        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        var intersectCount = tokens1.Count(tokens2.Contains);
        return (2.0 * intersectCount) / (tokens1.Count + tokens2.Count);
    }

    public static double NormalizedLevenshtein(string? s1, string? s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
            return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0.0;

        var maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0)
            return 1.0;

        var dist = LevenshteinDistance(s1, s2);
        return 1.0 - ((double)dist / maxLen);
    }

    public static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
