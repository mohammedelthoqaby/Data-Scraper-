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
    public void ParseAdvertisedPrice_ReadsPriceFromTitle(string title, decimal expected)
    {
        Assert.Equal(expected, AdPriceParser.ParseAdvertisedPrice(title, null));
    }

    [Theory]
    [InlineData("Lavazza 250g beans")]                          // grams are not a price
    [InlineData("Caffitaly 2026 edition")]                      // years are excluded
    [InlineData("Sample 12 EGP")]                               // below the plausible-price floor
    public void ParseAdvertisedPrice_NoPrice_ReturnsNull(string title)
    {
        Assert.Null(AdPriceParser.ParseAdvertisedPrice(title, null));
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
