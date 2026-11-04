using LazyBean.Application.Collection.Normalization;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

// Titles below are copied verbatim from tests/fixtures/shopify_coffeezonemisr_page1.json -
// a genuine capture of https://coffeezonemisr.com/products.json on 2026-11-03 - so these
// are the real strings the Shopify adapter has to cope with, not made-up examples.
public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Caffitaly System Cappuccino Bevande Espresso Capsules - 10 Capsules", 10)]
    [InlineData("Kimbo Espresso Aluminium Nespresso Capsules (Ristretto-Napoli-100%Arabica) - 100 Capsules", 100)]
    [InlineData("Kimbo Barista Napoli Espresso Aluminium Nespresso Capsules - 10 Capsules", 10)]
    [InlineData("Kimbo Barista 100% Arabica Espresso Aluminium Nespresso Capsules - 10 Capsules", 10)]
    [InlineData("COFFEA CLUB Espresso Coffee Capsules - 10 Capsules", 10)]
    [InlineData("L'OR Coffee L'OR FORZA Capsule - 1 Pack of 10 Capsules", 10)]
    [InlineData("Davidoff Rich Aroma Espresso Coffee Nespresso Capsules - 10 Capsules", 10)]
    [InlineData("Twix Dolce Gusto Hot Chocolate Capsules – Box of 8 Pods/8 Servings", 8)]
    [InlineData("Lavazza Mix Nespresso Capsules Bundle - 10 Packs of 100 Capsules", 100)]
    [InlineData("De'Longhi Magnifica Start Bean-to-Cup Fully Automatic Espresso Machine (1.8 Liters)", null)]
    public void ExtractUnitCount_RealShopifyTitles(string title, int? expected)
    {
        Assert.Equal(expected, TitleNormalizer.ExtractUnitCount(title));
    }

    [Theory]
    [InlineData("Borbone 100% Arabica Whole Beans Coffee 1kg", 1000)]
    [InlineData("Lavazza Crema e Gusto Ground Coffee 250g", 250)]
    [InlineData("illy Intenso Bold Roast Ground Espresso Coffee 250g", 250)]
    [InlineData("Lavazza Crema e Gusto Ground Coffee 250g (silver Pack)", 250)]
    [InlineData("Coffee Zone Turkish Coffee Light or Medium Roast ( Spiced ) - 1 kg (4 pack - 250 gm)", 1000)]
    [InlineData("Davidoff Espresso 57 Ground Espresso Coffee - 250 g", 250)]
    [InlineData("Davidoff Fine Aroma Ground Espresso Coffee - 250g", 250)]
    [InlineData("De'Longhi Magnifica Start Bean-to-Cup Fully Automatic Espresso Machine (1.8 Liters)", null)]
    public void ExtractWeightGrams_RealShopifyTitles(string title, int? expected)
    {
        Assert.Equal(expected, TitleNormalizer.ExtractWeightGrams(title));
    }

    [Theory]
    [InlineData("Caffitaly System Cappuccino Bevande Espresso Capsules - 10 Capsules")]
    [InlineData("Borbone 100% Arabica Whole Beans Coffee 1kg")]
    [InlineData("Lavazza Crema e Gusto Ground Coffee 250g")]
    public void Decode_RealShopifyTitles_HasNoHtmlEntities(string title)
    {
        var decoded = TitleNormalizer.Decode(title);
        Assert.DoesNotContain("&amp;", decoded);
        Assert.DoesNotContain("&#", decoded);
    }

    [Theory]
    [InlineData("&amp;Lavazza Qualit&#224; Oro&amp;", "&Lavazza Qualità Oro&")]
    public void Decode_HtmlEntities_AreDecoded(string raw, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Decode(raw));
    }

    [Theory]
    [InlineData("Lavazza  Crema   e Gusto", "lavazza crema e gusto")]
    [InlineData("  Trim Me  ", "trim me")]
    public void NormalizeForMatching_CollapsesWhitespaceAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.NormalizeForMatching(input));
    }

    [Theory]
    [InlineData("قهوة لافازا أورو ٢٥٠ جرام", 250)]
    [InlineData("كبسولات اسبريسو - ١٠ كبسولات", null)]
    public void ExtractWeightGrams_ArabicIndicDigits(string title, int? expected)
    {
        Assert.Equal(expected, TitleNormalizer.ExtractWeightGrams(title));
    }

    [Theory]
    [InlineData("كبسولات اسبريسو - ١٠ كبسولات", 10)]
    [InlineData("عشرون كبسولة", null)] // spelled-out numbers aren't handled - digits only.
    public void ExtractUnitCount_ArabicIndicDigits(string title, int? expected)
    {
        Assert.Equal(expected, TitleNormalizer.ExtractUnitCount(title));
    }

    [Theory]
    [InlineData("قهوة أمريكية", "قهوه امريكيه")]
    [InlineData("إبريق قهوة آلي", "ابريق قهوه الي")]
    [InlineData("قهوة مُحمّصة", "قهوه محمصه")] // tashkeel stripped
    public void NormalizeForMatching_ArabicLetterVariants(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.NormalizeForMatching(input));
    }
}
