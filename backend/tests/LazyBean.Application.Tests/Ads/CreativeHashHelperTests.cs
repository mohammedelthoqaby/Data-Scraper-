using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class CreativeHashHelperTests
{
    [Fact]
    public void Compute_SameContent_ProducesSameHash()
    {
        var hash1 = CreativeHashHelper.Compute("Special Coffee Offer", "Get 20% off all beans", "https://example.com/img1.png");
        var hash2 = CreativeHashHelper.Compute("  Special Coffee Offer  ", "get 20% off all beans", "https://example.com/img1.png ");

        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length);
    }

    [Fact]
    public void Compute_DifferentContent_ProducesDifferentHash()
    {
        var hash1 = CreativeHashHelper.Compute("Offer A", "Body A");
        var hash2 = CreativeHashHelper.Compute("Offer B", "Body B");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Compute_NullOrEmptyFields_HandledGracefully()
    {
        var hash = CreativeHashHelper.Compute(null, null, null);
        Assert.NotNull(hash);
        Assert.Equal(16, hash.Length);
    }
}
