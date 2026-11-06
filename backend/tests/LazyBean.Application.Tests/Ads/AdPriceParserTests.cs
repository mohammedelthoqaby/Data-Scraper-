using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Titles/bodies are the same shape as real Meta ad copy seen in the Ads page (Arabic and
// English, Western and Arabic-Indic digits).
public class AdPriceParserTests
{
    [Theory]
    [InlineData("٤٩٠ جنيه", 490)]                              // Arabic-Indic digits + Arabic currency
    [InlineData("L'OR Capsules 490 EGP", 490)]                  // trailing EGP
    [InlineData("EGP 490 L'OR capsules", 490)]                  // leading EGP
    [InlineData("Lor capsules بـ 490 جنيه", 490)]               // Arabic "bi-" price marker
    [InlineData("Kimbo for 349 EGP", 349)]                      // English "for" marker
    [InlineData("Promo 350 instead of 420", 350)]               // promo price wins over the old price
    [InlineData("Caffitaly capsules بسعر ٢٧٥ جنيه", 275)]        // mixed Arabic text + Arabic-Indic digits
    [InlineData("Espresso machine 1,250 EGP", 1250)]            // thousands separator
    [InlineData("١٣٬٩٩٩ جنيه", 13999)]                           // Arabic thousands separator (U+066C)
    [InlineData("٨٬٩٨٩ جنيه", 8989)]                             // Arabic thousands separator, four digits
    [InlineData("Espresso 13.999 EGP", 13999)]                  // dot as thousands separator, three digits after it
    [InlineData("Espresso 13,999 EGP", 13999)]                  // comma as thousands separator
    [InlineData("Lor Capsules 490 ج.م", 490)]                   // Egyptian currency abbreviation
    public void ParseAdvertisedPrice_ReadsPriceFromTitle(string title, decimal expected)
    {
        Assert.Equal(expected, AdPriceParser.ParseAdvertisedPrice(title, null));
    }

    [Theory]
    [InlineData("٨٬٩٨٩", 8989)]
    [InlineData("13,999", 13999)]
    [InlineData("13.999", 13999)]
    [InlineData("20.5", 20.5)]
    [InlineData("١٣٬٩٩٩", 13999)]
    public void ParseAmount_ReadsNumbersWithSeparators(string text, decimal expected)
    {
        Assert.Equal(expected, AdPriceParser.ParseAmount(text));
    }

    [Theory]
    [InlineData("Lavazza 250g beans")]                          // grams are not a price
    [InlineData("Caffitaly 2026 edition")]                      // years are excluded
    [InlineData("Sample 12 EGP")]                               // below the plausible-price floor
    [InlineData("Philips 20 مشروب")]                            // 20 drinks, not 20 EGP
    [InlineData("Capsules 50 كبسولة")]                          // 50 capsules
    [InlineData("Coffee 500 جرام")]                             // 500 grams
    [InlineData("Beans 1 كيلو")]                                // 1 kilo
    [InlineData("Machine 3 سنة ضمان")]                          // 3 years of warranty
    [InlineData("Offer 12 شهر تقسيط")]                          // 12 months
    [InlineData("Save 20% today")]                              // percent
    [InlineData("Pack 2x larger")]                              // "x" as a multiplier
    public void ParseAdvertisedPrice_NoPrice_ReturnsNull(string title)
    {
        Assert.Null(AdPriceParser.ParseAdvertisedPrice(title, null));
    }

    [Fact]
    public void ParseAdvertisedPrice_UnitNumberDoesNotHideALaterPrice()
    {
        // "20 drinks" is skipped, and the real price after it is found.
        Assert.Equal(490m, AdPriceParser.ParseAdvertisedPrice("Philips 20 مشروب بـ 490 جنيه", null));
    }

    [Fact]
    public void ParseOffer_ArabicBundle_ReadsThePackPriceAndQuantity()
    {
        // "أي علبتين بـ 490": any two boxes for 490, so 245 each.
        var offer = AdPriceParser.ParseOffer("أي علبتين بـ 490", null);

        Assert.NotNull(offer);
        Assert.Equal(490m, offer.Price);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(245m, offer.UnitPrice);
    }

    [Fact]
    public void ParseOffer_ArabicBundleWithCapsuleCount_ReadsTheQuantityAndThePackPrice()
    {
        // "any two boxes, 10 capsules, for 490": the capsule count is not the quantity.
        var offer = AdPriceParser.ParseOffer("أي علبتين ١٠ كبسولات بـ ٤٩٠ جنيه", null);

        Assert.NotNull(offer);
        Assert.Equal(490m, offer.Price);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(245m, offer.UnitPrice);
    }

    [Fact]
    public void ParseOffer_PacksOfTen_TakesThePackCountNotTheCapsuleCount()
    {
        // "2 packs of 10": two packs, ten capsules each. The quantity is the two packs.
        var offer = AdPriceParser.ParseOffer("2 packs of 10 for 490 EGP", null);

        Assert.NotNull(offer);
        Assert.Equal(490m, offer.Price);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(245m, offer.UnitPrice);
    }

    [Fact]
    public void ParseOffer_BundleWithAnOldPrice_KeepsTheQuantity()
    {
        var offer = AdPriceParser.ParseOffer("Any 2 packs of 10 capsules for 490 EGP instead of 520", null);

        Assert.NotNull(offer);
        Assert.Equal(490m, offer.Price);
        Assert.Equal(2, offer.Quantity);
    }

    [Fact]
    public void ParseOffer_EnglishPackBundle_ReadsThePackPriceAndQuantity()
    {
        var offer = AdPriceParser.ParseOffer("5 packs for 750", null);

        Assert.NotNull(offer);
        Assert.Equal(750m, offer.Price);
        Assert.Equal(5, offer.Quantity);
        Assert.Equal(150m, offer.UnitPrice);
    }

    [Fact]
    public void ParseOffer_TwoForPrice_ReadsThePackPriceAndQuantity()
    {
        var offer = AdPriceParser.ParseOffer("2 for 490 EGP", null);

        Assert.NotNull(offer);
        Assert.Equal(490m, offer.Price);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(245m, offer.UnitPrice);
    }

    [Fact]
    public void ParseOffer_SinglePrice_HasQuantityOne()
    {
        var offer = AdPriceParser.ParseOffer("Lor capsules بـ 490 جنيه", null);

        Assert.NotNull(offer);
        Assert.Equal(1, offer.Quantity);
        Assert.Equal(490m, offer.UnitPrice);
    }

    [Fact]
    public void ParseAdvertisedPrice_FallsBackToBodyWhenTitleHasNoPrice()
    {
        Assert.Equal(275m, AdPriceParser.ParseAdvertisedPrice("Espresso capsules", "عرض خاص 275 جنيه"));
    }

    [Fact]
    public void ParseAdvertisedPrice_TitlePriceTakesPrecedenceOverBody()
    {
        Assert.Equal(490m, AdPriceParser.ParseAdvertisedPrice("490 EGP", "300 EGP"));
    }

    [Fact]
    public void HasPriceDiscrepancy_DifferentPrices_IsTrue()
    {
        Assert.True(AdPriceParser.HasPriceDiscrepancy(259m, 275m));
    }

    [Fact]
    public void HasPriceDiscrepancy_EqualPrices_IsFalse()
    {
        Assert.False(AdPriceParser.HasPriceDiscrepancy(275m, 275m));
    }

    [Fact]
    public void HasPriceDiscrepancy_MissingAdvertisedPrice_IsFalse()
    {
        Assert.False(AdPriceParser.HasPriceDiscrepancy(null, 275m));
    }

    [Fact]
    public void HasPriceDiscrepancy_MissingStorePrice_IsFalse()
    {
        Assert.False(AdPriceParser.HasPriceDiscrepancy(259m, null));
    }
}
