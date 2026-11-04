using LazyBean.Application.Collection.Classification;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class ProductClassifierTests
{
    private readonly ProductClassifier _classifier = new();

    [Theory]
    // 1. CoffeeBeans (English & Arabic)
    [InlineData("Borbone 100% Arabica Whole Beans Coffee 1kg", null, CanonicalCategory.CoffeeBeans)]
    [InlineData("Lavazza Espresso Italiano Cremoso Whole Coffee Beans - 1kg", null, CanonicalCategory.CoffeeBeans)]
    [InlineData("Eilles Coffee Rostmeister Multitalent Whole Coffee Beans 1 Hg", null, CanonicalCategory.CoffeeBeans)]
    [InlineData("Marcafe - Crema Bar Coffee Beans - 1kg", null, CanonicalCategory.CoffeeBeans)]
    [InlineData("Pellini Gran Aroma Beans 1kg | حبوب بيليني جران أروما", null, CanonicalCategory.CoffeeBeans)]
    [InlineData("بن حبوب كاملة اسبريسو ارابيكا 100%", null, CanonicalCategory.CoffeeBeans)]

    // 2. GroundCoffee (English & Arabic)
    [InlineData("Kimbo - Ground Coffee - Napoli - 250g", null, CanonicalCategory.GroundCoffee)]
    [InlineData("Granell Ground Coffee 250g – Mango", null, CanonicalCategory.GroundCoffee)]
    [InlineData("بن جرانيل مطحون – مانجو Granell Ground Coffee 250g – Mango", null, CanonicalCategory.GroundCoffee)]
    [InlineData("قهوة مطحونة فلتر اسبريسو 250 جم", null, CanonicalCategory.GroundCoffee)]

    // 3. TurkishCoffee (English & Arabic)
    [InlineData("Nino Turkish Coffee Medium Plain 250g", null, CanonicalCategory.TurkishCoffee)]
    [InlineData("نينو قهوة تركية وسط سادة 125جرام – Nino Turkish Coffee", null, CanonicalCategory.TurkishCoffee)]
    [InlineData("بن عبد المعبود محوج فاتح 200 جرام", null, CanonicalCategory.TurkishCoffee)]
    [InlineData("بن العميد قهوة تركية سادة وسط 250 غرام", null, CanonicalCategory.TurkishCoffee)]
    [InlineData("بن شاهين تركي محوج غامق 250 جم", null, CanonicalCategory.TurkishCoffee)]

    // 4. InstantCoffee (English & Arabic)
    [InlineData("Nescafé Gold Blend Instant Coffee 200g", null, CanonicalCategory.InstantCoffee)]
    [InlineData("3 in 1 Classic Instant Coffee Sticks", null, CanonicalCategory.InstantCoffee)]
    [InlineData("نسكافيه جولد سريعة التحضير 100 جم", null, CanonicalCategory.InstantCoffee)]
    [InlineData("قهوة سريعة الذوبان 2 في 1 بدون سكر", null, CanonicalCategory.InstantCoffee)]

    // 5. CapsulesNespressoOriginal (English & Arabic)
    [InlineData("illy Forte Extra Bold Roast Espresso Aluminium Nespresso Capsules - 1 Pack, 10 Pcs", null, CanonicalCategory.CapsulesNespressoOriginal)]
    [InlineData("GOAT - Black Espresso Compatible By Nespresso-12 Aluminum Capsules", null, CanonicalCategory.CapsulesNespressoOriginal)]
    [InlineData("كبسولات متوافقة مع ماكينات نسبريسو 10 كبسولات", null, CanonicalCategory.CapsulesNespressoOriginal)]

    // 6. CapsulesNespressoVertuo (English & Arabic)
    [InlineData("Nespresso Vertuo Line Double Espresso Chiaro - 10 Pods", null, CanonicalCategory.CapsulesNespressoVertuo)]
    [InlineData("كبسولات نسبريسو فيرتو ماستر اوريجين كولومبيا", null, CanonicalCategory.CapsulesNespressoVertuo)]

    // 7. CapsulesDolceGusto (English & Arabic)
    [InlineData("NESCAFÉ Dolce Gusto Capsules, Café Au Lait – Box of 16 Pods/16 Servings", null, CanonicalCategory.CapsulesDolceGusto)]
    [InlineData("كبسولات دولتشي جوستو كابتشينو - 16 كبسولة", null, CanonicalCategory.CapsulesDolceGusto)]

    // 8. CapsulesOther (English & Arabic)
    [InlineData("Caffitaly System Cappuccino Bevande Espresso Capsules - 10 Capsules", null, CanonicalCategory.CapsulesOther)]
    [InlineData("Lavazza A Modo Mio Passionale - 16 Capsules", null, CanonicalCategory.CapsulesOther)]
    [InlineData("كبسولات كافيتالي انتنسو فيفاشي 10 كبسولات", null, CanonicalCategory.CapsulesOther)]

    // 9. Equipment (English & Arabic)
    [InlineData("De'Longhi Dedica Duo EC890.PK Espresso & Cold Brew Machine - Pink + Gifts", null, CanonicalCategory.Equipment)]
    [InlineData("Bialetti Mini Express Pure Gold Moka Pot 2 Cups", null, CanonicalCategory.Equipment)]
    [InlineData("Creyke H40 Manual/Electric Coffee Grinder 40mm | 40mmكريكي مطحنة قهوة يدوية وكهربائية-H40", null, CanonicalCategory.Equipment)]
    [InlineData("Espresso Machine Solac Squissita Supremma 20 Bar – ماكينة اسبريسو احترافية ضغط عالي تحضير مثالي", null, CanonicalCategory.Equipment)]
    [InlineData("Stainless Steel Glass Moka Pot | إبريق موكا من الستانلس ستيل والزجاج", null, CanonicalCategory.Equipment)]

    // 10. Accessories (English & Arabic)
    [InlineData("Coffee Spoon With Clip", null, CanonicalCategory.Accessories)]
    [InlineData("Nespresso Origin Collection Gran Lungo - 2 Mugs - 270 ml", null, CanonicalCategory.Accessories)]
    [InlineData("كوب جرعات رقمى دائرى لون أسود – Coffee Dosing Cup", null, CanonicalCategory.Accessories)]
    [InlineData("فرشاة تنظيف خشبى بقاعدة نحاس Code:2495", null, CanonicalCategory.Accessories)]
    [InlineData("ميزان بلاك ميرور ديجيتال الذكي لتحضير القهوة", null, CanonicalCategory.Accessories)]

    // 11. Other (English & Arabic)
    [InlineData("Brodies Green Tea With Lemon - 20 Tea Bags", null, CanonicalCategory.Other)]
    [InlineData("Lotus Biscoff 50 Individual Pieces", null, CanonicalCategory.Other)]
    [InlineData("Class A Stroop Wafel Coated with Dark Chocolate 41g", null, CanonicalCategory.Other)]
    [InlineData("شاى اخضر برويديز 20 كيس", null, CanonicalCategory.Other)]
    [InlineData("سيرب فانيليا مونين 700 مل", null, CanonicalCategory.Other)]
    [InlineData("أقراص تنظيف ماكينة القهوة وإزالة الترسبات", null, CanonicalCategory.Other)]
    public void Classify_RealTitles_AssignsCorrectCanonicalCategory(string title, string? productType, CanonicalCategory expectedCategory)
    {
        var result = _classifier.Classify(title, productType: productType);
        Assert.Equal(expectedCategory, result.Category);
        Assert.True(result.Confidence >= 0.85m, $"Expected confidence >= 0.85 for '{title}', got {result.Confidence}");
        Assert.False(string.IsNullOrWhiteSpace(result.MatchedRule));
    }

    [Fact]
    public void Classify_CoffeeLikeAmbiguousTitle_FlagsForReviewWithLowConfidence()
    {
        // An ambiguous title containing coffee terms but no clear category match
        var title = "Mystery Specialty Coffee Gift Collection 2026";
        var result = _classifier.Classify(title);

        Assert.Equal(CanonicalCategory.Other, result.Category);
        Assert.True(result.RequiresReview);
        Assert.Contains("coffee", result.ReviewReason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Confidence < 0.60m);
    }

    [Fact]
    public void Classify_NegativePatterns_PreventCapsuleMatchingForMachines()
    {
        var title = "Nespresso Inissia Espresso Machine, Black";
        var result = _classifier.Classify(title);

        Assert.Equal(CanonicalCategory.Equipment, result.Category);
        Assert.NotEqual(CanonicalCategory.CapsulesNespressoOriginal, result.Category);
    }

    [Fact]
    public void Classify_ConfigurableJson_CanBeTunedWithoutRecompilation()
    {
        var customJson = @"{
          ""rules"": [
            {
              ""id"": ""custom_tea"",
              ""category"": ""Other"",
              ""priority"": 999,
              ""confidence"": 0.99,
              ""titlePatterns"": [ ""\\bcustomtea\\b"" ],
              ""negativeTitlePatterns"": [],
              ""productTypePatterns"": [],
              ""tagPatterns"": [],
              ""vendorPatterns"": []
            }
          ]
        }";

        var customClassifier = new ProductClassifier(customJson);
        var result = customClassifier.Classify("Premium Customtea Herbal");

        Assert.Equal(CanonicalCategory.Other, result.Category);
        Assert.Equal("custom_tea", result.MatchedRule);
        Assert.Equal(0.99m, result.Confidence);
    }
}
