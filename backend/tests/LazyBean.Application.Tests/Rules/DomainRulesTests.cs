using LazyBean.Domain.Rules;
using Xunit;

namespace LazyBean.Application.Tests.Rules;

public class DomainRulesTests
{
    [Theory]
    [InlineData("12345", 2, 123.45)]
    [InlineData("49000", 2, 490)]
    [InlineData("7", 0, 7)]
    public void Money_reads_minor_units_as_the_currency(string value, int digits, decimal expected) =>
        Assert.Equal(expected, Money.FromMinorUnits(value, digits));

    [Fact]
    public void An_ad_seen_on_the_cutoff_counts_as_running_and_one_seen_before_it_has_ended()
    {
        var now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var cutoff = AdActivity.CutoffFor(now, AdActivity.DefaultWindowDays);

        Assert.Equal(now.AddDays(-30), cutoff);

        // The listing rule keeps an ad seen on or after the cutoff.
        Assert.True(AdActivity.IsRunning(cutoff, cutoff));
        Assert.False(AdActivity.IsRunning(cutoff.AddSeconds(-1), cutoff));

        // The sync rule ends an ad last seen on or before the cutoff.
        Assert.True(AdActivity.IsExpired(cutoff, cutoff));
        Assert.True(AdActivity.IsExpired(cutoff.AddSeconds(-1), cutoff));
        Assert.False(AdActivity.IsExpired(cutoff.AddSeconds(1), cutoff));
    }
}
