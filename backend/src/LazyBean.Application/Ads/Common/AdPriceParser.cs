using System.Globalization;
using System.Text.RegularExpressions;
using LazyBean.Application.Collection.Normalization;

namespace LazyBean.Application.Ads.Common;

// What an ad advertises as a price. A number is a price only when a currency word or a price word sits next to it,
// and never when a unit follows it (drinks, capsules, grams, years, months, percent, "x"). A bundle such as
// "5 packs for 750" keeps the pack price and the quantity; the store price is compared with the price per unit.
public sealed record AdPriceOffer(decimal Price, int Quantity)
{
    // Price per unit, rounded to 2 decimals: 490 for 2 is 245 each.
    public decimal UnitPrice => Math.Round(Price / Quantity, 2, MidpointRounding.AwayFromZero);
}

public static partial class AdPriceParser
{
    // "X instead of Y" or "X بدلا من Y": X is the promotional price.
    [GeneratedRegex(
        @"(?<![\d.,])(?<promo>\d[\d.,]*)\s*(?:instead\s+of|بدلا\s+من|بدل)\s*(?:egp|le|ج\.م|جنيه|جنية)?\s*(?<old>\d[\d.,]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsteadOfPattern();

    // A bundle: "5 packs for 750", "أي علبتين بـ 490", "2 for 490 EGP", "3 بسعر 250 جنيه".
    [GeneratedRegex(
        @"(?<![\d.,])(?<!\bof\s)(?<quantity>\d{1,2}|علبتين|علبتان|اتنين|اثنين|ثنتين|ثلاث\w*)\s*(?:packs?|pieces?|pcs|boxes|box|علب\w*|عبوات|عبوة|كرتونة|كراتين|باكيت\w*)?(?:\s*of\s*\d{1,3}(?:\s*(?:capsules?|pods?|cups?|pcs|pieces?|كبسول\w*|حبة|حبات))?)?(?:\s*\d{1,3}\s*(?:كبسول\w*|capsules?|pods?|cups?|pcs|pieces?|حبة|حبات))?\s*(?:for|بسعر|ب|مقابل|only|فقط)\s*(?<price>\d[\d.,]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiPackPattern();

    // A number with optional thousands separators: "1,250", "13.999", "13,999", "20.5".
    [GeneratedRegex(
        @"(?<![\d.,])\d{1,3}(?:[.,]\d{3})+(?!\d)|(?<![\d.,])\d+(?:[.,]\d{1,2})?(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    // A currency word right after the number: "490 EGP", "490 LE", "490 L.E", "490 ج.م", "490 جنيه".
    [GeneratedRegex(
        @"^\s*(?:egp\b|le\b|l\.e\b|ج\.م|جنيه|جنية|pounds?\b|£)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyAfterPattern();

    // A currency word right before the number: "EGP 490", "LE 490", "جنيه 490", "ج.م 490".
    [GeneratedRegex(
        @"(?:\begp|\ble|\bl\.e|ج\.م|جنيه|جنية|£)\s*[:=]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyBeforePattern();

    // A price word right before the number: "بـ 490" (the tatweel is removed first), "بسعر 490", "for 490", "only 490".
    [GeneratedRegex(
        @"(?:^|\s)(?:بسعر|سعر|ب|for|only|فقط|at)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PriceWordBeforePattern();

    // A unit right after a number: drinks, capsules, grams, litres, years, months, days, percent, "x".
    // "جم" is grams here: it is not a currency word in this parser.
    [GeneratedRegex(
        @"^\s*(?:%|×|x\b|kg\b|g\b|gr\b|gm\b|grams?\b|ml\b|l(?![\w'’])|ltr\b|litres?\b|liters?\b|لتر|ليتر|كيلو|جرام\w*|جم\b|مشروب\w*|كبسول\w*|capsules?\b|pods?\b|cups?\b|drinks?\b|سنة|سنه|سنين|سنوات|years?\b|شهر|شهور|شهرين|أشهر|اشهر|months?\b|يوم|أيام|ايام|days?\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitAfterPattern();

    // A pack word right after a number: "5 packs", "2 علب", "4 عبوات". The number is then a quantity, not a price.
    [GeneratedRegex(
        @"^\s*(?:packs?\b|pieces?\b|pcs\b|boxes\b|box\b|علب\w*|عبوات|عبوة|كرتونة|كراتين|باكيت\w*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackWordAfterPattern();

    public static decimal? ParseAdvertisedPrice(string? title, string? body) => ParseOffer(title, body)?.Price;

    // The price an ad advertises, with its quantity: a single price has quantity 1, a bundle has its pack count.
    // The title is read first (promotions and bundles are announced in titles), then the body.
    public static AdPriceOffer? ParseOffer(string? title, string? body) =>
        ParseOfferFromText(title) ?? ParseOfferFromText(body);

    // Reads a number written with Arabic-Indic digits and separators, or with ASCII digits and separators:
    // "٨٬٩٨٩" -> 8989, "13.999" -> 13999, "13,999" -> 13999, "20.5" -> 20.5. Returns null if the text is not a number.
    public static decimal? ParseAmount(string? text)
    {
        var normalized = Normalize(text).Trim();
        var match = NumberPattern().Match(normalized);
        return match.Success && match.Length == normalized.Length ? ParseNumber(match.Value) : null;
    }

    public static bool HasPriceDiscrepancy(decimal? advertisedPrice, decimal? storePrice)
    {
        if (!advertisedPrice.HasValue || !storePrice.HasValue)
        {
            return false;
        }

        return Math.Abs(advertisedPrice.Value - storePrice.Value) > 0.01m;
    }

    private static AdPriceOffer? ParseOfferFromText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var text = Normalize(rawText);

        // 1. A bundle: the pack price for a stated quantity greater than one.
        var multi = MultiPackPattern().Match(text);
        if (multi.Success &&
            ParseQuantity(multi.Groups["quantity"].Value) is { } quantity && quantity > 1 &&
            ParseNumber(multi.Groups["price"].Value) is { } bundlePrice &&
            IsPlausiblePrice(bundlePrice))
        {
            return new AdPriceOffer(bundlePrice, quantity);
        }

        // 2. The promotional price in "X instead of Y".
        var insteadOf = InsteadOfPattern().Match(text);
        if (insteadOf.Success &&
            ParseNumber(insteadOf.Groups["promo"].Value) is { } promo &&
            IsPlausiblePrice(promo))
        {
            return new AdPriceOffer(promo, 1);
        }

        // 3. A single price: a number with a currency word or a price word next to it, and no unit or pack word after it.
        foreach (Match number in NumberPattern().Matches(text))
        {
            var after = text[(number.Index + number.Length)..];
            var before = text[..number.Index];

            if (PackWordAfterPattern().IsMatch(after) || UnitAfterPattern().IsMatch(after))
            {
                continue;
            }

            if (ParseNumber(number.Value) is not { } value || !IsPlausiblePrice(value))
            {
                continue;
            }

            if (CurrencyAfterPattern().IsMatch(after) ||
                CurrencyBeforePattern().IsMatch(before) ||
                PriceWordBeforePattern().IsMatch(before))
            {
                return new AdPriceOffer(value, 1);
            }
        }

        return null;
    }

    // Arabic-Indic digits become ASCII, Arabic separators become ASCII separators, and the tatweel is removed
    // so that "بـ" reads as "ب".
    private static string Normalize(string? raw)
    {
        var decoded = TitleNormalizer.Decode(raw ?? string.Empty);
        return ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded)
            .Replace('٬', ',')
            .Replace('٫', '.')
            .Replace("ـ", string.Empty);
    }

    private static decimal? ParseNumber(string token)
    {
        // "1,250" and "13.999" are thousands: three digits after the separator.
        if (Regex.IsMatch(token, @"^\d{1,3}(?:[.,]\d{3})+$"))
        {
            return decimal.Parse(
                token.Replace(".", string.Empty).Replace(",", string.Empty),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        return decimal.TryParse(token.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? ParseQuantity(string token)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
        {
            return digits;
        }

        if (token.StartsWith("ثلاث", StringComparison.Ordinal))
        {
            return 3;
        }

        return token is "علبتين" or "علبتان" or "اتنين" or "اثنين" or "ثنتين" ? 2 : null;
    }

    private static bool IsPlausiblePrice(decimal price)
    {
        // Phone numbers, years and grams are not prices. Realistic advertised retail prices for coffee in Egypt
        // run from 20 EGP to 60,000 EGP.
        return price >= 20m && price <= 60000m && price != 2024m && price != 2025m && price != 2026m;
    }
}
