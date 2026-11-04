using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Adapters;
using Xunit;

namespace LazyBean.IntegrationTests.Collection;

// Offline against tests/fixtures/shopify_coffeezonemisr_page1.json - a genuine capture of
// https://coffeezonemisr.com/products.json, so these assertions are against real data.
public class ShopifyAdapterTests
{
    private static readonly Competitor Competitor = new()
    {
        Key = "coffee-zone-misr",
        BaseUrl = "https://coffeezonemisr.com",
        Platform = CompetitorPlatform.Shopify,
        MinDelayMs = 0
    };

    [Fact]
    public async Task FetchAsync_RealFixture_MapsFirstProductCorrectly()
    {
        var http = new FakeCollectorHttpClient()
            .When("page=1", Fixtures.Read("shopify_coffeezonemisr_page1.json"))
            .When("page=2", Fixtures.Read("shopify_empty_page.json"));

        var adapter = new ShopifyAdapter(http);

        var products = new List<Application.Collection.Contracts.RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        Assert.Equal(30, products.Count);

        var first = products[0];
        Assert.Equal("Caffitaly System Cappuccino Bevande Espresso Capsules - 10 Capsules", first.Title);
        Assert.Equal(
            "https://coffeezonemisr.com/products/caffitaly-system-cappuccino-bevande-espresso-capsules-10-capsules",
            first.CanonicalUrl);
        Assert.Single(first.Variants);

        var variant = first.Variants[0];
        Assert.Equal(215.00m, variant.Price);
        Assert.Equal(235.00m, variant.CompareAtPrice);
        Assert.Equal("EGP", variant.Currency);
        Assert.True(variant.InStock);
    }

    [Fact]
    public async Task FetchAsync_StopsPaginating_OnEmptyPage()
    {
        var http = new FakeCollectorHttpClient()
            .When("page=1", Fixtures.Read("shopify_coffeezonemisr_page1.json"))
            .When("page=2", Fixtures.Read("shopify_empty_page.json"));

        var adapter = new ShopifyAdapter(http);

        var count = 0;
        await foreach (var _ in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(30, count);
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=1"));
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=2"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.Contains("page=3"));
    }

    [Fact]
    public async Task FetchAsync_RepeatsSamePageForever_StopsPaginatingAndMarksDegraded()
    {
        // A handler that returns the same page 1 capture for every page request
        var http = new FakeCollectorHttpClient()
            .When("products.json", Fixtures.Read("shopify_coffeezonemisr_page1.json"));

        using var _ = Application.Collection.Orchestration.CollectionRunContext.Begin(Guid.NewGuid());
        var adapter = new ShopifyAdapter(http);

        var products = new List<Application.Collection.Contracts.RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        // Must yield products from page 1 only (30)
        Assert.Equal(30, products.Count);
        // Must request page 1 and page 2, but stop before page 3
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=1"));
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=2"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.Contains("page=3"));
        // Must mark the run context degraded
        Assert.True(Application.Collection.Orchestration.CollectionRunContext.IsDegraded);
        Assert.Contains("Pagination loop detected", Application.Collection.Orchestration.CollectionRunContext.DegradedReason);
    }

    [Fact]
    public async Task FetchAsync_PageCapReached_StopsAndMarksDegraded()
    {
        var http = new FakeCollectorHttpClient()
            .When("page=1", Fixtures.Read("shopify_coffeezonemisr_page1.json"))
            .When("page=2", Fixtures.Read("shopify_coffeezonemisr_page1.json"));

        using var _ = Application.Collection.Orchestration.CollectionRunContext.Begin(Guid.NewGuid());
        // Configure page cap of 1
        var adapter = new ShopifyAdapter(http, maxPages: 1);

        var products = new List<Application.Collection.Contracts.RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        Assert.Equal(30, products.Count);
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=1"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.Contains("page=2"));
        Assert.True(Application.Collection.Orchestration.CollectionRunContext.IsDegraded);
        Assert.Contains("Page cap", Application.Collection.Orchestration.CollectionRunContext.DegradedReason);
    }
}
