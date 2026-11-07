using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Admins paste either the advertiser ID or an Ads Transparency link. Both must reduce to the same ID, and anything
// else must be refused so a wrong value never reaches the collector.
public class GoogleAdvertiserReferenceTests
{
    [Theory]
    [InlineData("AR01064852753525768193", "AR01064852753525768193")]
    [InlineData("ar01064852753525768193", "AR01064852753525768193")]
    [InlineData("  AR12066457051832778753  ", "AR12066457051832778753")]
    [InlineData("https://adstransparency.google.com/advertiser/AR01064852753525768193?region=EG", "AR01064852753525768193")]
    [InlineData("adstransparency.google.com/advertiser/AR12066457051832778753", "AR12066457051832778753")]
    [InlineData("https://adstransparency.google.com.eg/advertiser/AR12066457051832778753/creative/CR1?region=EG", "AR12066457051832778753")]
    public void ValidIdOrLink_ReducesToTheAdvertiserId(string input, string expected)
    {
        Assert.True(GoogleAdvertiserReference.TryParse(input, out var advertiserId));
        Assert.Equal(expected, advertiserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cafelax.com")]
    [InlineData("AR123")]
    [InlineData("AR0106485275352576819")]                       // 19 digits
    [InlineData("AR010648527535257681930")]                      // 21 digits
    [InlineData("AR0106485275352576819١")]                  // Arabic-Indic digit is not an ASCII digit
    [InlineData("https://adstransparency.google.com/advertiser/AR123")]
    [InlineData("https://adstransparency.google.com/advertiser/AR010648527535257681930")]
    [InlineData("https://www.facebook.com/lazybeanonline")]
    public void AnythingElse_IsRefused(string? input)
    {
        Assert.False(GoogleAdvertiserReference.TryParse(input, out var advertiserId));
        Assert.Equal(string.Empty, advertiserId);
    }
}
