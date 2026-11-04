using LazyBean.Application.Matching;
using Xunit;

namespace LazyBean.Application.Tests.Matching;

public class TitleSimilarityTests
{
    [Fact]
    public void ExtractTokens_RemovesStopwordsAndPunctuation()
    {
        var tokens = TitleSimilarity.ExtractTokens("Lavazza - Espresso Italiano Ground Coffee 250g");
        Assert.Contains("lavazza", tokens);
        Assert.Contains("italiano", tokens);
        Assert.Contains("250g", tokens);
        Assert.DoesNotContain("coffee", tokens);
        Assert.DoesNotContain("espresso", tokens);
        Assert.DoesNotContain("-", tokens);
    }

    [Fact]
    public void TokenDiceSimilarity_IdenticalTokens_ReturnsOne()
    {
        var t1 = TitleSimilarity.ExtractTokens("Lavazza Crema e Gusto 250g");
        var t2 = TitleSimilarity.ExtractTokens("Lavazza Crema e Gusto 250g");

        var sim = TitleSimilarity.TokenDiceSimilarity(t1, t2);
        Assert.Equal(1.0, sim);
    }

    [Fact]
    public void TokenDiceSimilarity_CompletelyDifferent_ReturnsZero()
    {
        var t1 = TitleSimilarity.ExtractTokens("Lavazza Crema e Gusto");
        var t2 = TitleSimilarity.ExtractTokens("Davidoff Rich Aroma");

        var sim = TitleSimilarity.TokenDiceSimilarity(t1, t2);
        Assert.Equal(0.0, sim);
    }

    [Fact]
    public void NormalizedLevenshtein_CalculatesAccurately()
    {
        var sim = TitleSimilarity.NormalizedLevenshtein("bristot arabica", "bristot arabica");
        Assert.Equal(1.0, sim);

        var sim2 = TitleSimilarity.NormalizedLevenshtein("bristot arabica", "bristot espresso");
        Assert.True(sim2 > 0.4 && sim2 < 0.9);
    }
}
