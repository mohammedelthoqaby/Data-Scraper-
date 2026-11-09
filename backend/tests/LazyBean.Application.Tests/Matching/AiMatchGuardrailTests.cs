using LazyBean.Application.Matching;
using LazyBean.Application.Matching.AiMatching;
using LazyBean.Domain.Enums;
using Xunit;
using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Tests.Matching;

/// <summary>Hard guardrails that override any AI verdict, plus the pair cache key.</summary>
public class AiMatchGuardrailTests
{
    private static MatchCandidate Candidate(
        string title,
        int? weightGrams = null,
        int? unitCount = null,
        string? capsule = null,
        decimal price = 0m,
        string beverage = "Coffee") => new()
    {
        Title = title,
        NormalizedTitle = title.ToLowerInvariant(),
        Brand = "Lavazza",
        CanonicalCategory = CanonicalCategory.CoffeeBeans,
        BeverageType = beverage,
        WeightGrams = weightGrams,
        UnitCount = unitCount,
        CapsuleSystem = capsule,
        Price = price,
        CanonicalUrl = "https://example.test/products/lavazza",
        Tokens = TitleSimilarity.ExtractTokens(title),
    };

    [Fact]
    public void Guardrail_Rejects_250g_Versus_1kg()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Qualita Oro 250g", weightGrams: 250),
            Candidate("Lavazza Qualita Oro 1kg", weightGrams: 1000));

        Assert.True(verdict.Blocked);
        Assert.Contains("different size 250g vs 1000g", verdict.Reason);
    }

    [Fact]
    public void Guardrail_Allows_Weights_Within_two_percent()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Qualita Oro 250g", weightGrams: 250),
            Candidate("Lavazza Qualita Oro 252g", weightGrams: 252));

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void Guardrail_Rejects_Beans_Versus_Ground()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Qualita Oro Whole Beans 250g", weightGrams: 250),
            Candidate("Lavazza Qualita Oro Ground 250g", weightGrams: 250));

        Assert.True(verdict.Blocked);
        Assert.Contains("different form", verdict.Reason);
    }

    [Fact]
    public void Guardrail_Rejects_Nespresso_Versus_Dolce_Gusto()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Qualita Oro Capsules 10", unitCount: 10, capsule: "Nespresso"),
            Candidate("Lavazza Qualita Oro Capsules 10", unitCount: 10, capsule: "Dolce Gusto"));

        Assert.True(verdict.Blocked);
        Assert.Contains("different capsule system", verdict.Reason);
    }

    [Fact]
    public void Guardrail_Rejects_Different_Capsule_Count()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Capsules 10", unitCount: 10, capsule: "Nespresso"),
            Candidate("Lavazza Capsules 16", unitCount: 16, capsule: "Nespresso"));

        Assert.True(verdict.Blocked);
        Assert.Contains("different pack size", verdict.Reason);
    }

    [Fact]
    public void Guardrail_Allows_Bundle_When_Unit_Price_Normalizes_And_Marks_It()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Oro 2 x 250g bundle", weightGrams: 250, unitCount: 2, price: 500m),
            Candidate("Lavazza Oro 2 x 250g", weightGrams: 250, unitCount: 2, price: 480m));

        Assert.False(verdict.Blocked);
        Assert.True(verdict.IsBundle);
    }

    [Fact]
    public void Guardrail_Rejects_Bundle_When_Unit_Price_Cannot_Be_Normalized()
    {
        var verdict = AiMatchGuardrails.Evaluate(
            Candidate("Lavazza Oro bundle with free mug", weightGrams: 250, price: 0m),
            Candidate("Lavazza Oro 250g", weightGrams: 250, price: 300m));

        Assert.True(verdict.Blocked);
        Assert.Contains("unit price cannot be normalized", verdict.Reason);
    }

    [Fact]
    public void PairHash_Ignores_Price_But_Changes_When_Size_Changes()
    {
        var reference = Candidate("Lavazza Qualita Oro 250g", weightGrams: 250, price: 300m);
        var cheaper = Candidate("Lavazza Qualita Oro 250g", weightGrams: 250, price: 250m);
        var resized = Candidate("Lavazza Qualita Oro 250g", weightGrams: 1000, price: 300m);
        var competitor = Candidate("Lavazza Qualita Oro 250g", weightGrams: 250, price: 280m);

        Assert.Equal(AiPairHasher.Compute(competitor, reference), AiPairHasher.Compute(competitor, cheaper));
        Assert.NotEqual(AiPairHasher.Compute(competitor, reference), AiPairHasher.Compute(competitor, resized));
    }
}
