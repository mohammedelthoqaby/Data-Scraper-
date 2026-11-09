using LazyBean.Infrastructure.External.Google;
using LazyBean.Infrastructure.External.Http;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Infrastructure.External.Woo;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Adapters;
using Xunit;

namespace LazyBean.IntegrationTests.Collection;

// Offline against tests/fixtures/woo_strongcoffee_*.json - a genuine capture of
// https://strongcoffee-eg.com/wp-json/wc/store/v1/products (see tests/fixtures/README.md).
public class WooStoreApiAdapterTests
{
    private static readonly Competitor Competitor = new()
    {
        Key = "strong-coffee",
        BaseUrl = "https://strongcoffee-eg.com",
        Platform = CompetitorPlatform.WooCommerce,
        MinDelayMs = 0
    };

    private static FakeCollectorHttpClient BuildHttp() => new FakeCollectorHttpClient()
        .When("products?per_page=100&page=1",
            Fixtures.Read("woo_strongcoffee_page1.json"),
            new Dictionary<string, string> { ["X-WP-TotalPages"] = "91" })
        .When("products/47645", Fixtures.Read("woo_strongcoffee_variation_47645.json"))
        .When("products/47646", Fixtures.Read("woo_strongcoffee_variation_47646.json"))
        .When("products/47647", Fixtures.Read("woo_strongcoffee_variation_47647.json"));

    private static async Task<List<Application.Collection.Contracts.RawProduct>> FetchAllAsync()
    {
        var adapter = new WooStoreApiAdapter(BuildHttp());
        var products = new List<Application.Collection.Contracts.RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }
        return products;
    }

    [Fact]
    public async Task FetchAsync_SimpleProduct_PassesThroughRawTitleWithHtmlEntityUndecoded()
    {
        var products = await FetchAllAsync();

        // Adapters hand back the raw title verbatim - HTML-decoding is the orchestrator's
        // job (TitleNormalizer.Decode), not the adapter's, so "&#8211;" is still here.
        var discounted = products.Single(p => p.ExternalId == "48170");
        Assert.Contains("&#8211;", discounted.Title);
        Assert.Contains("ميزان بلاك ميرور", discounted.Title);
    }

    [Fact]
    public async Task FetchAsync_DiscountedProduct_SetsCompareAtFromRegularPrice()
    {
        var products = await FetchAllAsync();

        // "price":"275000", "regular_price":"300000" at currency_minor_unit=2 -> 2750.00 / 3000.00 EGP
        var discounted = products.Single(p => p.ExternalId == "48170");
        var variant = discounted.Variants[0];
        Assert.Equal(2750.00m, variant.Price);
        Assert.Equal(3000.00m, variant.CompareAtPrice);
        Assert.Equal("EGP", variant.Currency);
        Assert.True(variant.InStock);
    }

    [Fact]
    public async Task FetchAsync_ArabicOnlyName_TitleAndWeightExtractionWorkOnRealData()
    {
        var products = await FetchAllAsync();

        // "Nino Preiata Espresso Coffee Beans 90% Arabica &#8211; 1kg", price 1100.00, compare 1200.00
        var nino = products.Single(p => p.ExternalId == "47625");
        var variant = nino.Variants[0];
        Assert.Equal(1100.00m, variant.Price);
        Assert.Equal(1200.00m, variant.CompareAtPrice);
    }

    [Fact]
    public async Task FetchAsync_VariableProduct_FetchesEachVariationForItsOwnPrice()
    {
        var products = await FetchAllAsync();

        var variable = products.Single(p => p.ExternalId == "47644");
        Assert.Equal(3, variable.Variants.Count);
        Assert.All(variable.Variants, v => Assert.Equal(2500.00m, v.Price)); // same sale price on all 3
        Assert.Contains(variable.Variants, v => v.ExternalId == "47645");
        Assert.Contains(variable.Variants, v => v.ExternalId == "47646");
        Assert.Contains(variable.Variants, v => v.ExternalId == "47647");
    }

    [Fact]
    public async Task FetchAsync_VariableProduct_VariantTitleComesFromListingAttributes()
    {
        var products = await FetchAllAsync();

        // The listing's variations[].attributes carries "color": "black"/"green"/"matte white" -
        // the detail endpoint's own attributes field is empty on this real site.
        var variable = products.Single(p => p.ExternalId == "47644");
        var black = variable.Variants.Single(v => v.ExternalId == "47645");
        Assert.Equal("black", black.Title);
    }

    [Fact]
    public async Task FetchAsync_StopsPaginating_AfterSinglePage_PerTotalPagesHeader()
    {
        // X-WP-TotalPages is 91 on the real site, but the fake handler only has page 1 mapped -
        // the adapter would request page 2 next; this just proves it read the header correctly
        // rather than looping forever on a 404/empty fallback from the fake handler.
        var http = BuildHttp();
        var adapter = new WooStoreApiAdapter(http);

        var count = 0;
        await foreach (var _ in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(20, count); // 20 products in the fixture page
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=1"));
        Assert.Contains(http.RequestedUrls, u => u.Contains("page=2")); // attempted, 404s on the fake handler
    }

    [Fact]
    public async Task FetchAsync_BulkVariations_UsesBulkEndpointAndAvoidsIndividualCalls()
    {
        var bulkVariationsJson = @"[
            {""id"":47645,""name"":""Var 47645"",""parent"":47644,""type"":""variation"",""variation"":""color: black"",""sku"":""SKU1"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}},
            {""id"":47646,""name"":""Var 47646"",""parent"":47644,""type"":""variation"",""variation"":""color: green"",""sku"":""SKU2"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}},
            {""id"":47647,""name"":""Var 47647"",""parent"":47644,""type"":""variation"",""variation"":""color: matte white"",""sku"":""SKU3"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}}
        ]";

        var http = new FakeCollectorHttpClient()
            .When("products?type=variation&per_page=100&page=1", bulkVariationsJson, new Dictionary<string, string> { ["X-WP-TotalPages"] = "1" })
            .When("products?per_page=100&page=1", Fixtures.Read("woo_strongcoffee_page1.json"), new Dictionary<string, string> { ["X-WP-TotalPages"] = "1" });

        var adapter = new WooStoreApiAdapter(http);
        var products = new List<Application.Collection.Contracts.RawProduct>();

        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        var variable = products.Single(p => p.ExternalId == "47644");
        Assert.Equal(3, variable.Variants.Count);

        // Crucial: assert that individual variation endpoints were NOT called
        Assert.Contains(http.RequestedUrls, u => u.Contains("type=variation"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47645"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47646"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47647"));
    }

    [Fact]
    public async Task FetchAsync_BulkVariations_PaginatesAcrossMultiplePagesUntilTotalPages()
    {
        var bulkPage1 = @"[
            {""id"":47645,""name"":""Var 47645"",""parent"":47644,""type"":""variation"",""variation"":""color: black"",""sku"":""SKU1"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}}
        ]";
        var bulkPage2 = @"[
            {""id"":47646,""name"":""Var 47646"",""parent"":47644,""type"":""variation"",""variation"":""color: green"",""sku"":""SKU2"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}},
            {""id"":47647,""name"":""Var 47647"",""parent"":47644,""type"":""variation"",""variation"":""color: matte white"",""sku"":""SKU3"",""is_in_stock"":true,""prices"":{""price"":""250000"",""regular_price"":""250000"",""currency_code"":""EGP"",""currency_minor_unit"":2}}
        ]";

        var http = new FakeCollectorHttpClient()
            .When("products?type=variation&per_page=100&page=1", bulkPage1, new Dictionary<string, string> { ["X-WP-TotalPages"] = "2" })
            .When("products?type=variation&per_page=100&page=2", bulkPage2, new Dictionary<string, string> { ["X-WP-TotalPages"] = "2" })
            .When("products?per_page=100&page=1", Fixtures.Read("woo_strongcoffee_page1.json"), new Dictionary<string, string> { ["X-WP-TotalPages"] = "1" });

        var adapter = new WooStoreApiAdapter(http);
        var products = new List<Application.Collection.Contracts.RawProduct>();

        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        var variable = products.Single(p => p.ExternalId == "47644");
        Assert.Equal(3, variable.Variants.Count);

        // Verify that both bulk pages were requested
        Assert.Contains(http.RequestedUrls, u => u.Contains("type=variation&per_page=100&page=1"));
        Assert.Contains(http.RequestedUrls, u => u.Contains("type=variation&per_page=100&page=2"));
        // Verify no individual calls were needed
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47645"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47646"));
        Assert.DoesNotContain(http.RequestedUrls, u => u.EndsWith("products/47647"));
    }

    [Fact]
    public async Task FetchAsync_PerCompetitorTimeout_StopsAndMarksDegraded()
    {
        using var scope = Application.Collection.Orchestration.CollectionRunContext.Begin(Guid.NewGuid());

        var http = new FakeCollectorHttpClient()
            .When("products?type=variation&per_page=100&page=1", "[]", new Dictionary<string, string> { ["X-WP-TotalPages"] = "1" })
            .When("products?per_page=100&page=1", Fixtures.Read("woo_strongcoffee_page1.json"), new Dictionary<string, string> { ["X-WP-TotalPages"] = "10" });

        // Timeout of 0ms triggers cancellation immediately
        var adapter = new WooStoreApiAdapter(http, timeout: TimeSpan.FromMilliseconds(0));

        var count = 0;
        await foreach (var _ in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            count++;
        }

        Assert.True(Application.Collection.Orchestration.CollectionRunContext.IsDegraded);
        Assert.Contains("timeout", Application.Collection.Orchestration.CollectionRunContext.DegradedReason ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
