using LazyBean.Application.Ads.Common;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class CreativeHashHelperTests
{
    [Fact]
    public void NormalizeBody_StripsUrlsHashtagsEmojisAndWhitespace()
    {
        var raw1 = "Enjoy UP TO 50% OFF on top products! ☕ https://www.cafelax.com/ #ShopAndSave";
        var raw2 = "enjoy up to 50% off on top products!   https://cafelax.com/offers #Coffee";

        var norm1 = CreativeHashHelper.NormalizeBody(raw1);
        var norm2 = CreativeHashHelper.NormalizeBody(raw2);

        Assert.Equal("enjoy up to 50% off on top products!", norm1);
        Assert.Equal("enjoy up to 50% off on top products!", norm2);
    }

    [Fact]
    public void Compute_SameIdentityAndNormalizedBody_ProducesSameCampaignHash()
    {
        var identityId = Guid.NewGuid();
        var body1 = "Discover your favorite coffee ☕ https://shop.com #coffee";
        var body2 = "  discover your favorite coffee   https://other.com/deal #deal";

        var hash1 = CreativeHashHelper.Compute(identityId, body1);
        var hash2 = CreativeHashHelper.Compute(identityId, body2);

        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length);
    }

    [Fact]
    public void Compute_DifferentIdentities_ProduceDifferentCampaignHash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var body = "Summer Special Coffee Offer";

        var hash1 = CreativeHashHelper.Compute(id1, body);
        var hash2 = CreativeHashHelper.Compute(id2, body);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ExtractProductHandle_ShopifyAndWooCommerce_ExtractsHandle()
    {
        var shopify1 = "https://www.cafelax.com/products/lavazza-espresso-can-250g?variant=123";
        var shopify2 = "https://cafelax.com/collections/beans/products/melitta-grand-aroma-250g";
        var woo = "https://strongcoffee-eg.com/product/lor-lungo-capsules-10-capsules/";

        Assert.Equal("lavazza-espresso-can-250g", CreativeHashHelper.ExtractProductHandle(shopify1));
        Assert.Equal("melitta-grand-aroma-250g", CreativeHashHelper.ExtractProductHandle(shopify2));
        Assert.Equal("lor-lungo-capsules-10-capsules", CreativeHashHelper.ExtractProductHandle(woo));
    }

    [Fact]
    public void NormalizeProductUrl_StripsWwwAndQuery()
    {
        var url1 = "https://www.cafelax.com/products/lavazza-can?utm_source=fb&variant=123";
        var url2 = "https://cafelax.com/products/lavazza-can";

        Assert.Equal("https://cafelax.com/products/lavazza-can", CreativeHashHelper.NormalizeProductUrl(url1));
        Assert.Equal("https://cafelax.com/products/lavazza-can", CreativeHashHelper.NormalizeProductUrl(url2));
    }
}
