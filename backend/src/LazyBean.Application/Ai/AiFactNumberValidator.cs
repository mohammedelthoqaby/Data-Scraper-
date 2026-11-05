using System.Globalization;
using System.Text.RegularExpressions;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Collection.Normalization;

namespace LazyBean.Application.Ai;

public static class AiFactNumberValidator
{
    private static readonly Regex NumberRegex = new(
        @"[\d\u0660-\u0669]+(?:\.[\d\u0660-\u0669]+)?",
        RegexOptions.Compiled);

    public static bool ValidateNumbers(AttentionFactItem fact, string headline, string headlineAr, string explanation, string explanationAr)
    {
        var text = $"{headline} {headlineAr} {explanation} {explanationAr}";
        var extractedNumbers = ExtractNumbers(text);

        if (extractedNumbers.Count == 0)
            return true;

        var allowed = fact.Numbers.Select(n => Math.Round(n, 2)).ToHashSet();
        // Also allow common integers like 1, 2, 3 (e.g. rank, item count) or rounded versions of fact numbers
        foreach (var n in fact.Numbers)
        {
            allowed.Add(Math.Round(n, 0));
            allowed.Add(Math.Round(n, 1));
        }

        foreach (var num in extractedNumbers)
        {
            var rounded = Math.Round(num, 2);
            var rounded0 = Math.Round(num, 0);
            var rounded1 = Math.Round(num, 1);

            // If number doesn't match any allowed fact number
            if (!allowed.Contains(rounded) && !allowed.Contains(rounded0) && !allowed.Contains(rounded1))
            {
                return false;
            }
        }

        return true;
    }

    public static List<decimal> ExtractNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = NumberRegex.Matches(text);
        var results = new List<decimal>();

        foreach (Match m in matches)
        {
            var raw = m.Value;
            var normalized = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(raw);

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                results.Add(val);
            }
        }

        return results;
    }
}
