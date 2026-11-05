using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Matching;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Matching;

public class ProductMatcherTests
{
    private static ProductMatcher.MatchCandidate CreateCandidate(
        string title,
        string? vendor,
        CanonicalCategory category,
        string url = "https://example.com/product")
    {
        var weight = TitleNormalizer.ExtractWeightGrams(title);
        var unit = TitleNormalizer.ExtractUnitCount(title, category);
        var vol = TitleNormalizer.ExtractVolumeMl(title);
        var brand = ProductMatcher.DetectBrand(vendor, title);
        var capsule = TitleNormalizer.ExtractCapsuleSystem(title, category);
        var beverage = TitleNormalizer.ExtractBeverageType(title, category);
        var dist = ProductMatcher.ExtractDistinguishingTokens(title, brand, weight, unit, vol, category);

        return new ProductMatcher.MatchCandidate
        {
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString(),
            Title = title,
            NormalizedTitle = TitleNormalizer.NormalizeForMatching(title),
            Vendor = vendor,
            CanonicalCategory = category,
            WeightGrams = weight,
            UnitCount = unit,
            VolumeMl = vol,
            CanonicalUrl = url,
            Brand = brand,
            CapsuleSystem = capsule,
            BeverageType = beverage,
            DistinguishingTokens = dist,
            Tokens = TitleSimilarity.ExtractTokens(title)
        };
    }

    [Fact]
    public void WrongPair1_LavazzaExpertCremaRicca_vs_LavazzaExpertCremaAndAroma_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Lavazza Expert Crema Ricca 1kg", "Lavazza", CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("Lavazza Expert Crema & Aroma 1kg", "Lavazza", CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void WrongPair2_MovenpickRistrettoNespressoCompatible_vs_NespressoRistretto_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Movenpick Ristretto Nespresso Compatible", null, CanonicalCategory.CapsulesNespressoOriginal);
        var reference = CreateCandidate("Nespresso Ristretto", "Nespresso", CanonicalCategory.CapsulesNespressoOriginal);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
        Assert.Equal("movenpick", comp.Brand);
        Assert.Equal("nespresso", reference.Brand);
    }

    [Fact]
    public void WrongPair3_MoninRubyChocolateSyrup_vs_MoninChocolateSyrup_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Monin Ruby Chocolate Syrup", "Monin", CanonicalCategory.Other);
        var reference = CreateCandidate("Monin Chocolate Syrup", "Monin", CanonicalCategory.Other);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void WrongPair4_NespressoIspirazioneVenezia_vs_NespressoIspirazioneRoma_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Nespresso Ispirazione Venezia", "Nespresso", CanonicalCategory.CapsulesNespressoOriginal);
        var reference = CreateCandidate("Nespresso Ispirazione Roma", "Nespresso", CanonicalCategory.CapsulesNespressoOriginal);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void WrongPair5_LavazzaProntoCremaGrandeAroma_vs_LavazzaExpertCremaAndAroma_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Lavazza Pronto Crema Grande Aroma 1kg", "Lavazza", CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("Lavazza Expert Crema & Aroma 1kg", "Lavazza", CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void WrongPair6_StarbucksBlondeEspressoByNespresso_vs_StarbucksEspressoRoastByNespresso_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Starbucks Blonde Espresso by Nespresso", "Starbucks", CanonicalCategory.CapsulesNespressoOriginal);
        var reference = CreateCandidate("Starbucks Espresso Roast by Nespresso", "Starbucks", CanonicalCategory.CapsulesNespressoOriginal);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void WrongPair7_DavidoffFineAroma90g_vs_100g_MustBeSizeVariant_NeverAuto()
    {
        var comp = CreateCandidate("Davidoff Fine Aroma 90g", "Davidoff", CanonicalCategory.InstantCoffee);
        var reference = CreateCandidate("Davidoff Fine Aroma 100g", "Davidoff", CanonicalCategory.InstantCoffee);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
        Assert.Equal(MatchDecision.SizeVariant, eval.Decision);
    }

    [Fact]
    public void CorrectPair1_MoninKiwiSyrup700ml_MustKeepAutoMatching()
    {
        var comp = CreateCandidate("Monin Kiwi Syrup 700ml", "Monin", CanonicalCategory.Other);
        var reference = CreateCandidate("Monin Kiwi Syrup – 700ml", "Monin", CanonicalCategory.Other);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.Auto, eval.Decision);
        Assert.True(eval.Score >= 0.95m);
    }

    [Fact]
    public void CorrectPair2_NespressoVolluto10caps_MustKeepAutoMatching()
    {
        var comp = CreateCandidate("Nespresso Volluto 10 caps", "Nespresso", CanonicalCategory.CapsulesNespressoOriginal);
        var reference = CreateCandidate("Nespresso Volluto Original Coffee Capsules – 10 Capsules", "Nespresso", CanonicalCategory.CapsulesNespressoOriginal);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.Auto, eval.Decision);
        Assert.True(eval.Score >= 0.95m);
    }

    [Fact]
    public void CorrectPair3_LavazzaQualitaRossaGround250g_MustKeepAutoMatching()
    {
        var comp = CreateCandidate("Lavazza Qualità Rossa Ground 250g", "Lavazza", CanonicalCategory.GroundCoffee);
        var reference = CreateCandidate("Lavazza Qualità Rossa Ground Coffee – 250g", "Lavazza", CanonicalCategory.GroundCoffee);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.Auto, eval.Decision);
        Assert.True(eval.Score >= 0.95m);
    }

    [Fact]
    public void CorrectPair4_KaromaRelaxHerbalTeaDolceGusto16_MustKeepAutoMatching()
    {
        var comp = CreateCandidate("Karoma Relax Herbal Tea Dolce Gusto 16", "Karoma", CanonicalCategory.CapsulesDolceGusto);
        var reference = CreateCandidate("Karoma Relax Herbal Tea Dolce Gusto® Compatible Capsules – 16 Capsules", "Karoma", CanonicalCategory.CapsulesDolceGusto);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.Auto, eval.Decision);
        Assert.True(eval.Score >= 0.95m);
    }

    [Fact]
    public void CorrectPair5_VergnanoRiservaTorino10caps_MustKeepAutoMatching()
    {
        var comp = CreateCandidate("Caffè Vergnano Riserva Torino 100% Arabica Coffee Capsules - 10 Capsules", "Caffè Vergnano", CanonicalCategory.CapsulesNespressoOriginal);
        var reference = CreateCandidate("Caffè Vergnano Riserva Torino 100% Arabica Nespresso Capsules – 10 Capsules", "Caffè Vergnano", CanonicalCategory.CapsulesNespressoOriginal);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.Auto, eval.Decision);
        Assert.True(eval.Score >= 0.95m);
    }

    [Fact]
    public void BrandUnknown_OnEitherSide_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Mystery Artisan Dark Roast Beans 250g", null, CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("Artisan Dark Roast Beans 250g", null, CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void MokaPotDifferentCupCapacity_MustBeSizeVariant_NeverAuto()
    {
        var comp = CreateCandidate("Bialetti Brikka Moka Pot 4 Cups", "Bialetti", CanonicalCategory.Equipment);
        var reference = CreateCandidate("Bialetti Brikka Moka Pot – 2 Cups", "Bialetti", CanonicalCategory.Equipment);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
        Assert.Equal(MatchDecision.SizeVariant, eval.Decision);
    }

    [Fact]
    public void MultiPackDifferentWeight_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Lavazza Crema E Gusto Classico Ground Coffee 2 X 250g - 500g", "Lavazza", CanonicalCategory.GroundCoffee);
        var reference = CreateCandidate("Lavazza Crema E Gusto Classico Ground Coffee – 250g", "Lavazza", CanonicalCategory.GroundCoffee);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void CoffeeSyrupVsChocolateSyrup_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Monin Coffee Syrup 700 ml", "Monin", CanonicalCategory.Other);
        var reference = CreateCandidate("Monin Chocolate Syrup – 700ml", "Monin", CanonicalCategory.Other);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void GroundCoffee_vs_WholeBeansCoffee_MustNotAutoAccept()
    {
        var comp = CreateCandidate("Illy Ground Coffee Espresso - Espresso Classico 250 grams", "illy", CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("illy Whole Bean Classico Coffee – 250g", "illy", CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void SizeUnknownOnEitherSide_MustNeverAutoAccept()
    {
        var comp = CreateCandidate("Manutti Peach Puree 1250g", "Manutti", CanonicalCategory.Other);
        var reference = CreateCandidate("Manutti Peach Puree", "Manutti", CanonicalCategory.Other);
        reference.WeightGrams = null;
        reference.VolumeMl = null;
        reference.UnitCount = null;

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.NotEqual(MatchDecision.Auto, eval.Decision);
    }

    [Fact]
    public void DistinguishingTokensEntirelyDifferent_Brasile_vs_Selection_MustBeNoMatch_AndNotEnterReviewQueue()
    {
        var comp = CreateCandidate(
            "Lavazza La Reserva de Tierra Brasile 100 % Arabica Espresso Whole Beans Coffee - 1 Kg",
            "Lavazza",
            CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate(
            "Lavazza La Reserva de Tierra Selection Espresso Whole Beans Coffee - 1 Kg",
            "Lavazza",
            CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);

        Assert.Equal(MatchDecision.None, eval.Decision);
    }
}
