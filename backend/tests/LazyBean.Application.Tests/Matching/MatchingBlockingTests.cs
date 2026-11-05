using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Matching;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Matching;

public class MatchingBlockingTests
{
    private static ProductMatcher.MatchCandidate CreateCandidate(
        string title,
        string? vendor,
        CanonicalCategory category,
        int? weight = null,
        int? unit = null)
    {
        var w = weight ?? TitleNormalizer.ExtractWeightGrams(title);
        var u = unit ?? TitleNormalizer.ExtractUnitCount(title, category);
        var vol = TitleNormalizer.ExtractVolumeMl(title);
        var brand = ProductMatcher.DetectBrand(vendor, title);
        var capsule = TitleNormalizer.ExtractCapsuleSystem(title, category);
        var beverage = TitleNormalizer.ExtractBeverageType(title, category);
        var dist = ProductMatcher.ExtractDistinguishingTokens(title, brand, w, u, vol, category);

        return new ProductMatcher.MatchCandidate
        {
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString(),
            Title = title,
            NormalizedTitle = TitleNormalizer.NormalizeForMatching(title),
            Vendor = vendor,
            CanonicalCategory = category,
            WeightGrams = w,
            UnitCount = u,
            VolumeMl = vol,
            CanonicalUrl = "https://example.com/p",
            Brand = brand,
            CapsuleSystem = capsule,
            BeverageType = beverage,
            DistinguishingTokens = dist,
            Tokens = TitleSimilarity.ExtractTokens(title)
        };
    }

    [Fact]
    public void CandidateBlocking_UnbrandedCompetitor_YieldsNoMatch()
    {
        // Unbranded competitor product (generic coffee with no detected brand)
        var comp = CreateCandidate("Fresh Roasted Coffee Special Blend 250g", null, CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("Lavazza Crema e Aroma 250g", "Lavazza", CanonicalCategory.CoffeeBeans);

        Assert.Null(comp.Brand);
        Assert.NotNull(reference.Brand);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);
        Assert.Equal(MatchDecision.None, eval.Decision);
    }

    [Fact]
    public void CandidateBlocking_NoSizeCompetitor_YieldsNoMatch()
    {
        // Competitor product without any detectable weight, volume, or unit count
        var comp = new ProductMatcher.MatchCandidate
        {
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            Title = "Lavazza Qualita Oro Coffee",
            Brand = "lavazza",
            CanonicalCategory = CanonicalCategory.CoffeeBeans,
            WeightGrams = null,
            VolumeMl = null,
            UnitCount = null,
            DistinguishingTokens = ["qualita", "oro"],
            Tokens = ["lavazza", "qualita", "oro", "coffee"]
        };

        var reference = CreateCandidate("Lavazza Qualita Oro 250g", "Lavazza", CanonicalCategory.CoffeeBeans);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);
        Assert.Equal(MatchDecision.None, eval.Decision);
    }

    [Fact]
    public void CandidateBlocking_FormatMismatch_BeansVsGround_YieldsNoMatch()
    {
        var comp = CreateCandidate("Lavazza Crema e Aroma Whole Beans 1kg", "Lavazza", CanonicalCategory.CoffeeBeans);
        var reference = CreateCandidate("Lavazza Crema e Aroma Ground Coffee 1kg", "Lavazza", CanonicalCategory.GroundCoffee);

        var eval = ProductMatcher.EvaluateMatch(comp, reference);
        Assert.Equal(MatchDecision.None, eval.Decision);
    }

    [Fact]
    public void ParseCompletedCategories_ParsesCorrectly()
    {
        var json = """
        [
            {"name": "classification", "status": "completed"},
            {"name": "matching", "status": "Running", "completedCategories": ["CoffeeBeans", "GroundCoffee", "TurkishCoffee"]}
        ]
        """;

        var completed = ProductMatcher.ParseCompletedCategories(json);

        Assert.Equal(3, completed.Count);
        Assert.Contains("CoffeeBeans", completed);
        Assert.Contains("GroundCoffee", completed);
        Assert.Contains("TurkishCoffee", completed);
        Assert.DoesNotContain("CapsulesNespressoOriginal", completed);
    }

    [Fact]
    public void ParseCompletedCategories_HandlesNullAndEmptyGracefully()
    {
        Assert.Empty(ProductMatcher.ParseCompletedCategories(null));
        Assert.Empty(ProductMatcher.ParseCompletedCategories(""));
        Assert.Empty(ProductMatcher.ParseCompletedCategories("[]"));
        Assert.Empty(ProductMatcher.ParseCompletedCategories("""[{"name": "matching", "status": "Running"}]"""));
    }
}
