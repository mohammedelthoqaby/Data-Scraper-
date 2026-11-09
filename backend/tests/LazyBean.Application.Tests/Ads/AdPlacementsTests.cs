using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class AdPlacementsTests
{
    [Fact]
    public void Normalize_lowercases_trims_and_drops_repeats_and_blanks()
    {
        var stored = AdPlacements.Normalize(new[] { "FACEBOOK", " instagram ", "facebook", "", "AUDIENCE_NETWORK" });

        Assert.Equal("facebook,instagram,audience_network", stored);
    }

    [Fact]
    public void Normalize_returns_null_when_nothing_is_recorded()
    {
        Assert.Null(AdPlacements.Normalize(null));
        Assert.Null(AdPlacements.Normalize(Array.Empty<string>()));
        Assert.Null(AdPlacements.Normalize(new[] { "", "  " }));
    }

    [Fact]
    public void Normalize_drops_values_with_characters_outside_letters_digits_and_underscores()
    {
        Assert.Equal("facebook", AdPlacements.Normalize(new[] { "facebook", "face book!", "x;drop" }));
    }

    [Fact]
    public void Parse_reads_back_what_normalize_stored()
    {
        var stored = AdPlacements.Normalize(new[] { "FACEBOOK", "INSTAGRAM" });

        Assert.Equal(new[] { "facebook", "instagram" }, AdPlacements.Parse(stored));
    }

    [Fact]
    public void Parse_of_nothing_is_empty()
    {
        Assert.Empty(AdPlacements.Parse(null));
        Assert.Empty(AdPlacements.Parse("  "));
    }
}
