using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Normalization;

// Pure functions over a raw product/variant title - HTML-decoding, a matching-key form
// (Arabic-normalized, lowercase, collapsed whitespace), and weight/unit-count extraction.
public static partial class TitleNormalizer
{
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:kg|كجم|كيلو(?:جرام|جم)?)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightKgPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:g\b|gm\b|gram[s]?|جرام|جم|غرام)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightGramsPattern();

    [GeneratedRegex(
        @"(\d+)\s*(?:x|\*)\s*(\d+(?:\.\d+)?)\s*(?:g\b|gm\b|gram[s]?|جرام|جم|غرام)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MultiPackGramsPattern();

    [GeneratedRegex(
        @"(\d+)\s*(?:x|\*)\s*(\d+(?:\.\d+)?)\s*(?:kg\b|كجم|كيلو)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MultiPackKgPattern();

    [GeneratedRegex(
        @"(\d+)\s*(?:capsules?|caps?|pods?|pcs?|pieces?|cups?|كبسول(?:ة|ه|ات)?|قطع(?:ة|ه|ا)?|أكواب|اكواب|فناجين|فنجان)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnitCountPattern();

    [GeneratedRegex(@"\b([1-9]\d?|100)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingCountPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:l\b|liters?|لتر)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeLiterPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:ml\b|مل)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeMlPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    public static string Decode(string? title) => WebUtility.HtmlDecode(title ?? string.Empty).Trim();

    // The form used to compare/match titles across competitors: decoded, Arabic-Indic
    // digits -> ASCII, Arabic letter variants collapsed, lowercased, whitespace collapsed.
    public static string NormalizeForMatching(string? title)
    {
        var decoded = Decode(title);
        var asciiDigits = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded);
        var normalizedLetters = ArabicTextNormalizer.NormalizeLetters(asciiDigits);
        var collapsed = WhitespacePattern().Replace(normalizedLetters, " ").Trim();
        return collapsed.ToLowerInvariant();
    }

    // "250g" / "1kg" / "250 جرام" / "١ كجم" -> grams. Checked kg before g so "1kg" isn't
    // mis-read as a bare "g" match.
    public static int? ExtractWeightGrams(string? title)
    {
        var text = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(Decode(title));

        var multiPackGrams = MultiPackGramsPattern().Match(text);
        if (multiPackGrams.Success)
        {
            var count = ParseNumber(multiPackGrams.Groups[1].Value);
            var weight = ParseNumber(multiPackGrams.Groups[2].Value);
            return (int)Math.Round(count * weight);
        }

        var multiPackKg = MultiPackKgPattern().Match(text);
        if (multiPackKg.Success)
        {
            var count = ParseNumber(multiPackKg.Groups[1].Value);
            var weight = ParseNumber(multiPackKg.Groups[2].Value);
            return (int)Math.Round(count * weight * 1000);
        }

        var kgMatch = WeightKgPattern().Match(text);
        if (kgMatch.Success)
        {
            return (int)Math.Round(ParseNumber(kgMatch.Groups[1].Value) * 1000);
        }

        var gramsMatch = WeightGramsPattern().Match(text);
        return gramsMatch.Success ? (int)Math.Round(ParseNumber(gramsMatch.Groups[1].Value)) : null;
    }

    // "10 Capsules" / "١٠ كبسولات" / "Dolce Gusto 16" -> 16.
    public static int? ExtractUnitCount(string? title, CanonicalCategory? category = null)
    {
        var text = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(Decode(title));
        var match = UnitCountPattern().Match(text);
        if (match.Success)
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        var isCapsule = category is CanonicalCategory.CapsulesNespressoOriginal or
                        CanonicalCategory.CapsulesNespressoVertuo or
                        CanonicalCategory.CapsulesDolceGusto or
                        CanonicalCategory.CapsulesOther ||
                        Regex.IsMatch(text, @"\b(?:capsules?|caps?|pods?|gusto|nespresso|vertuo|caffitaly)\b|كبسول", RegexOptions.IgnoreCase);

        if (isCapsule)
        {
            var trailing = TrailingCountPattern().Match(text);
            if (trailing.Success)
                return int.Parse(trailing.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return null;
    }

    // "700ml" / "1L" -> ml.
    public static int? ExtractVolumeMl(string? title)
    {
        var text = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(Decode(title));

        var literMatch = VolumeLiterPattern().Match(text);
        if (literMatch.Success)
        {
            return (int)Math.Round(ParseNumber(literMatch.Groups[1].Value) * 1000);
        }

        var mlMatch = VolumeMlPattern().Match(text);
        return mlMatch.Success ? (int)Math.Round(ParseNumber(mlMatch.Groups[1].Value)) : null;
    }

    public static string? ExtractCapsuleSystem(string? title, CanonicalCategory category)
    {
        if (category == CanonicalCategory.CapsulesNespressoVertuo)
            return "NespressoVertuo";
        if (category == CanonicalCategory.CapsulesDolceGusto)
            return "DolceGusto";
        if (category == CanonicalCategory.CapsulesNespressoOriginal)
            return "NespressoOriginal";

        var normalized = NormalizeForMatching(title);

        if (Regex.IsMatch(normalized, @"\b(?:vertuo|vertuoline)\b|فيرتو|فرتو", RegexOptions.IgnoreCase))
            return "NespressoVertuo";

        if (Regex.IsMatch(normalized, @"\b(?:dolce\s*gusto|dolcegusto)\b|دولتشي\s*جوستو|دولتشى\s*جوستو|دولتشي\s*غوستو", RegexOptions.IgnoreCase))
            return "DolceGusto";

        if (Regex.IsMatch(normalized, @"\b(?:caffitaly)\b|كافيتالي", RegexOptions.IgnoreCase))
            return "Caffitaly";

        if (Regex.IsMatch(normalized, @"\b(?:a\s*modo\s*mio)\b|مودو\s*ميو", RegexOptions.IgnoreCase))
            return "AModoMio";

        if (Regex.IsMatch(normalized, @"\b(?:espresso\s*point)\b|اسبريسو\s*بوينت", RegexOptions.IgnoreCase))
            return "EspressoPoint";

        if (Regex.IsMatch(normalized, @"\b(?:e\.?s\.?e\.?|44\s*mm)\b", RegexOptions.IgnoreCase))
            return "EsePods";

        if (Regex.IsMatch(normalized, @"\b(?:nespresso)\b|نسبريسو", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(normalized, @"\b(?:capsules?|caps?|pods?|compatible)\b|كبسول|متوافق", RegexOptions.IgnoreCase))
            return "NespressoOriginal";

        return null;
    }

    public static string ExtractBeverageType(string? title, CanonicalCategory category)
    {
        var normalized = NormalizeForMatching(title);

        if (category is CanonicalCategory.CoffeeBeans or CanonicalCategory.GroundCoffee or
            CanonicalCategory.TurkishCoffee or CanonicalCategory.InstantCoffee or
            CanonicalCategory.CapsulesNespressoOriginal or CanonicalCategory.CapsulesNespressoVertuo or
            CanonicalCategory.CapsulesDolceGusto or CanonicalCategory.CapsulesOther)
        {
            if (Regex.IsMatch(normalized, @"\b(?:tea|matcha|herbal|chai|infusion)\b|شاي|شاى|اعشاب|أعشاب", RegexOptions.IgnoreCase))
                return "Tea";
            if (Regex.IsMatch(normalized, @"\b(?:syrup|puree|sauce|crush)\b|سيرب|بيوريه|صوص", RegexOptions.IgnoreCase))
                return "Syrup";
            if (Regex.IsMatch(normalized, @"\b(?:chocolate|cocoa|cacao)\b|شوكولاتة|شوكولاته|كاكاو", RegexOptions.IgnoreCase))
                return "Chocolate";

            return "Coffee";
        }

        if (category == CanonicalCategory.Equipment) return "Equipment";
        if (category == CanonicalCategory.Accessories) return "Accessories";

        if (Regex.IsMatch(normalized, @"\b(?:tea|matcha|herbal|chai|infusion)\b|شاي|شاى|اعشاب|أعشاب", RegexOptions.IgnoreCase))
            return "Tea";
        if (Regex.IsMatch(normalized, @"\b(?:syrup|puree|sauce|crush)\b|سيرب|بيوريه|صوص", RegexOptions.IgnoreCase))
            return "Syrup";
        if (Regex.IsMatch(normalized, @"\b(?:chocolate|cocoa|cacao)\b|شوكولاتة|شوكولاته|كاكاو", RegexOptions.IgnoreCase))
            return "Chocolate";
        if (Regex.IsMatch(normalized, @"(?:coffee|espresso|cappuccino|latte|arabica|robusta|roast|beans?|قهو|بن|اسبريسو)", RegexOptions.IgnoreCase))
            return "Coffee";

        return "Other";
    }

    private static double ParseNumber(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
