using System.Globalization;
using System.Text.RegularExpressions;
using LazyBean.Application.Collection.Normalization;

namespace LazyBean.Application.Ads.Common;

public static partial class AdPriceParser
{
    // Regex for "X instead of Y" or "X بدلا من Y" where X is the promotional price
    [GeneratedRegex(
        @"(?:(?:egp|le|l\.e|ج\.م|جم|جنيه|جنية)\s*)?(\d{2,6}(?:[\.,]\d{1,2})?)\s*(?:instead\s+of|بدلا\s+من|بدل)\s*(?:(?:egp|le|l\.e|ج\.م|جم|جنيه|جنية)\s*)?(\d{2,6}(?:[\.,]\d{1,2})?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex InsteadOfPattern();

    // Regex for prices with trailing Egyptian currency symbols
    // e.g. "490 EGP", "490 LE", "490 L.E", "490 ج.م", "490 جنيه", "490 جنية"
    [GeneratedRegex(
        @"\b(\d{2,6}(?:[\.,]\d{1,2})?)\s*(?:egp|le|l\.e|ج\.م|جم|جنيه|جنية|جنيهه|pounds?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrailingCurrencyPattern();

    // Regex for prices with leading Egyptian currency symbols
    // e.g. "EGP 490", "LE 490", "جنيه 490", "ج.م 490"
    [GeneratedRegex(
        @"(?:egp|le|l\.e|ج\.م|جم|جنيه|جنية)\s*[:=]?\s*(\d{2,6}(?:[\.,]\d{1,2})?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingCurrencyPattern();

    // Regex for Egyptian prepositions / price markers
    // e.g. "بـ 490 جنيه", "بـ 490", "ب 490", "for 490", "only 490", "سعر 490", "بسعر 490"
    [GeneratedRegex(
        @"(?:^|\s)(?:بـ|بـ\s*|ب\s+|for\s+|only\s+|فقط\s+|سعر\s+|بسعر\s+)(\d{2,6}(?:[\.,]\d{1,2})?)(?:\s*(?:egp|le|l\.e|ج\.م|جم|جنيه|جنية|pounds?))?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PrepositionPattern();

    // "1,250" -> "1250": a comma followed by exactly three digits is a thousands separator, not a decimal.
    [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSeparatorPattern();

    public static decimal? ParseAdvertisedPrice(string? title, string? body)
    {
        // 1. Try title first (promotions and bundle prices are typically announced in ad titles)
        var price = ExtractPriceFromText(title);
        if (price.HasValue)
        {
            return price;
        }

        // 2. Fall back to body text
        return ExtractPriceFromText(body);
    }

    private static decimal? ExtractPriceFromText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        // Convert Arabic-Indic digits (٠-٩) to ASCII (0-9)
        var decoded = TitleNormalizer.Decode(rawText);
        var text = ThousandsSeparatorPattern().Replace(
            ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded), string.Empty);

        // 1. Priority: "X instead of Y" promo pattern (X is the promotional price)
        var insteadOfMatch = InsteadOfPattern().Match(text);
        if (insteadOfMatch.Success)
        {
            if (TryParsePrice(insteadOfMatch.Groups[1].Value, out var promoPrice) && IsPlausiblePrice(promoPrice))
            {
                return promoPrice;
            }
        }

        // 2. Preposition pattern e.g. "بـ 490 جنيه" or "for 490 EGP"
        var prepMatch = PrepositionPattern().Match(text);
        if (prepMatch.Success)
        {
            if (TryParsePrice(prepMatch.Groups[1].Value, out var prepPrice) && IsPlausiblePrice(prepPrice))
            {
                return prepPrice;
            }
        }

        // 3. Trailing currency pattern e.g. "490 جنيه" or "490 EGP"
        var trailingMatch = TrailingCurrencyPattern().Match(text);
        if (trailingMatch.Success)
        {
            if (TryParsePrice(trailingMatch.Groups[1].Value, out var trailingPrice) && IsPlausiblePrice(trailingPrice))
            {
                return trailingPrice;
            }
        }

        // 4. Leading currency pattern e.g. "EGP 490" or "ج.م 490"
        var leadingMatch = LeadingCurrencyPattern().Match(text);
        if (leadingMatch.Success)
        {
            if (TryParsePrice(leadingMatch.Groups[1].Value, out var leadingPrice) && IsPlausiblePrice(leadingPrice))
            {
                return leadingPrice;
            }
        }

        return null;
    }

    private static bool TryParsePrice(string value, out decimal price)
    {
        // Normalize commas or thousand separators if any
        var clean = value.Replace(",", ".").Trim();
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price);
    }

    private static bool IsPlausiblePrice(decimal price)
    {
        // Avoid matching phone numbers, years (e.g. 2026), or grams (e.g. 250g)
        // Realistic advertised retail price for coffee packs/bundles/machines in Egypt: 20 EGP to 60,000 EGP
        return price >= 20m && price <= 60000m && price != 2024m && price != 2025m && price != 2026m;
    }

    public static bool HasPriceDiscrepancy(decimal? advertisedPrice, decimal? storePrice)
    {
        if (!advertisedPrice.HasValue || !storePrice.HasValue)
        {
            return false;
        }

        return Math.Abs(advertisedPrice.Value - storePrice.Value) > 0.01m;
    }
}
