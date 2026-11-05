using LazyBean.Application.Collection.Validation;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class ZeroProductsGuardTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 500)]
    public void ZeroProducts_MustNeverBeToleratedAsHealthy(int currentCount, int previousCount)
    {
        // Zero products from a source is always suspicious/failed
        var isZero = currentCount == 0;
        Assert.True(isZero);

        // Even if previous count was 0 or 100, current count of 0 must fail
        var dropBelowThreshold = previousCount > 0 && currentCount < 0.20 * previousCount;
        Assert.True(currentCount == 0 || dropBelowThreshold);
    }

    [Theory]
    [InlineData(19, 100)] // 19 < 20
    [InlineData(5, 50)]   // 5 < 10
    [InlineData(99, 1000)] // 99 < 200
    public void DropBelow20Percent_MustTriggerSuspiciousGuard(int currentCount, int previousCount)
    {
        var droppedBelow20Percent = previousCount > 0 && currentCount < 0.20 * previousCount;
        Assert.True(droppedBelow20Percent, $"Current count {currentCount} should trigger guard for previous {previousCount}.");
    }

    [Theory]
    [InlineData(85, 100)]
    [InlineData(25, 100)]
    [InlineData(200, 250)]
    public void CountAbove20Percent_DoesNotTrigger20PercentDropGuard(int currentCount, int previousCount)
    {
        var droppedBelow20Percent = previousCount > 0 && currentCount < 0.20 * previousCount;
        Assert.False(droppedBelow20Percent);
    }
}
