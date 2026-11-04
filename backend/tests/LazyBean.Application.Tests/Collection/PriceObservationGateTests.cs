using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Collection.Validation;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class PriceObservationGateTests
{
    [Fact]
    public void Evaluate_PositivePriceEgpNoHistory_Accepts()
    {
        var result = PriceObservationGate.Evaluate(215m, "EGP", lastPrice: null);
        Assert.Equal(PriceObservationDecision.Accept, result.Decision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Evaluate_NonPositivePrice_SendsToReview(decimal price)
    {
        var result = PriceObservationGate.Evaluate(price, "EGP", lastPrice: null);
        Assert.Equal(PriceObservationDecision.SendToReview, result.Decision);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Evaluate_NonEgpCurrency_SendsToReview(string currency)
    {
        var result = PriceObservationGate.Evaluate(100m, currency, lastPrice: null);
        Assert.Equal(PriceObservationDecision.SendToReview, result.Decision);
    }

    [Fact]
    public void Evaluate_PriceWithin60PercentOfLast_Accepts()
    {
        // 100 -> 155 is a 55% increase, under the 60% threshold.
        var result = PriceObservationGate.Evaluate(155m, "EGP", lastPrice: 100m);
        Assert.Equal(PriceObservationDecision.Accept, result.Decision);
    }

    [Fact]
    public void Evaluate_PriceJumpOver60Percent_SendsToReview()
    {
        // 100 -> 165 is a 65% increase.
        var result = PriceObservationGate.Evaluate(165m, "EGP", lastPrice: 100m);
        Assert.Equal(PriceObservationDecision.SendToReview, result.Decision);
    }

    [Fact]
    public void Evaluate_PriceDropOver60Percent_SendsToReview()
    {
        var result = PriceObservationGate.Evaluate(30m, "EGP", lastPrice: 100m);
        Assert.Equal(PriceObservationDecision.SendToReview, result.Decision);
    }
}

public class PriceLogicTests
{
    [Fact]
    public void IsDiscounted_CompareAtGreaterThanPrice_IsTrue()
    {
        Assert.True(PriceLogic.IsDiscounted(price: 100m, compareAtPrice: 150m));
    }

    [Fact]
    public void IsDiscounted_CompareAtEqualToPrice_IsFalse() =>
        Assert.False(PriceLogic.IsDiscounted(price: 100m, compareAtPrice: 100m));

    [Fact]
    public void IsDiscounted_CompareAtLowerThanPrice_IsFalse() =>
        Assert.False(PriceLogic.IsDiscounted(price: 100m, compareAtPrice: 90m));

    [Fact]
    public void IsDiscounted_NoCompareAt_IsFalse() =>
        Assert.False(PriceLogic.IsDiscounted(price: 100m, compareAtPrice: null));
}

public class ProductRemovalPolicyTests
{
    [Fact]
    public void IsHealthy_NoHistory_IsTrue()
    {
        Assert.True(ProductRemovalPolicy.IsHealthy(currentProductCount: 5, recentCompletedRunProductCounts: []));
    }

    [Fact]
    public void IsHealthy_SmallDrop_IsTrue()
    {
        // avg 100, now 75 -> 25% drop, under the 30% threshold.
        Assert.True(ProductRemovalPolicy.IsHealthy(75, [100, 100, 100]));
    }

    [Fact]
    public void IsHealthy_SharpDrop_IsFalse()
    {
        // avg 100, now 50 -> 50% drop, over the 30% threshold.
        Assert.False(ProductRemovalPolicy.IsHealthy(50, [100, 100, 100]));
    }
}
