using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Adapters;
using Xunit;

namespace LazyBean.IntegrationTests.Collection;

public class LazzyBeanAdapterTests
{
    private static readonly Competitor Competitor = new()
    {
        Key = "lazy-bean",
        Name = "Lazy Bean",
        BaseUrl = "https://api.lazzybean.com",
        Platform = CompetitorPlatform.LazzyBeanApi,
        IsReference = true,
        MinDelayMs = 0
    };

    private const string CategoriesJson = @"{
        ""data"": [
            { ""id"": ""cat-coffee-123"", ""category"": ""Coffee"", ""description"": ""Coffee category"" },
            { ""id"": ""cat-acc-456"", ""category"": ""Accessories"", ""description"": ""Accessories category"" }
        ]
    }";

    private const string CoffeePage1Json = @"{
        ""data"": {
            ""data"": [
                {
                    ""id"": ""prod-uuid-1"",
                    ""name"": ""Bristot Espresso Whole Beans - 1 kg"",
                    ""price"": 1100,
                    ""beforeDiscount"": 1250,
                    ""stock"": 25,
                    ""weight"": 1000,
                    ""roastName"": ""Medium-Dark"",
                    ""flavorName"": ""Chocolate"",
                    ""vendor_id"": ""vendor-bristot-1"",
                    ""vendor"": { ""id"": ""vendor-bristot-1"", ""name"": ""Bristot"" },
                    ""category"": { ""id"": ""cat-coffee-123"", ""name"": ""Coffee"" },
                    ""sub_category"": { ""id"": ""sub-beans-1"", ""name"": ""Whole Beans"" }
                },
                {
                    ""id"": ""prod-uuid-2"",
                    ""name"": ""Nespresso Indonesia - 10 capsules"",
                    ""price"": 699,
                    ""beforeDiscount"": 699,
                    ""stock"": 0,
                    ""weight"": 0,
                    ""roastName"": ""Medium"",
                    ""vendor_id"": ""vendor-nespresso-2"",
                    ""vendor"": { ""id"": ""vendor-nespresso-2"", ""name"": ""Nespresso"" },
                    ""category"": { ""id"": ""cat-coffee-123"", ""name"": ""Coffee"" },
                    ""sub_category"": { ""id"": ""sub-caps-2"", ""name"": ""Capsules"" }
                }
            ],
            ""pagination"": {
                ""total"": 2,
                ""currentPage"": 1,
                ""perPage"": 100,
                ""lastPage"": 1
            }
        }
    }";

    // Contains prod-uuid-1 again (to test cross-category deduplication) + prod-uuid-3
    private const string AccPage1Json = @"{
        ""data"": {
            ""data"": [
                {
                    ""id"": ""prod-uuid-1"",
                    ""name"": ""Bristot Espresso Whole Beans - 1 kg"",
                    ""price"": 1100,
                    ""beforeDiscount"": 1100,
                    ""stock"": 25,
                    ""category"": { ""id"": ""cat-acc-456"", ""name"": ""Accessories"" }
                },
                {
                    ""id"": ""prod-uuid-3"",
                    ""name"": ""V60 Coffee Dripper 02"",
                    ""price"": 450,
                    ""beforeDiscount"": 500,
                    ""stock"": 10,
                    ""weight"": 200,
                    ""vendor_id"": ""vendor-hario-3"",
                    ""vendor"": { ""id"": ""vendor-hario-3"", ""company_name"": ""Hario"" },
                    ""category"": { ""id"": ""cat-acc-456"", ""name"": ""Accessories"" },
                    ""sub_category"": { ""id"": ""sub-drippers"", ""name"": ""Drippers"" }
                }
            ],
            ""pagination"": {
                ""total"": 2,
                ""currentPage"": 1,
                ""perPage"": 100,
                ""lastPage"": 1
            }
        }
    }";

    [Fact]
    public async Task FetchAsync_MapsCategoriesProductsAndDedupesById()
    {
        var http = new FakeCollectorHttpClient()
            .When("/categories", CategoriesJson)
            .When("category=cat-coffee-123", CoffeePage1Json)
            .When("category=cat-acc-456", AccPage1Json);

        var adapter = new LazzyBeanAdapter(http);

        var products = new List<RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        // Deduplication: prod-uuid-1 appeared in both categories, so total must be 3 (1, 2, 3), not 4.
        Assert.Equal(3, products.Count);

        // Product 1: Bristot (discounted, in stock, 1000g, medium-dark)
        var p1 = products[0];
        Assert.Equal("prod-uuid-1", p1.ExternalId);
        Assert.Equal("Bristot Espresso Whole Beans - 1 kg", p1.Title);
        Assert.Equal("https://lazzybean.com/single-product/prod-uuid-1", p1.CanonicalUrl);
        Assert.Equal("Coffee", p1.Category);
        Assert.Equal("Whole Beans", p1.ProductType);
        Assert.Equal("Bristot", p1.Vendor);
        Assert.Equal("vendor-bristot-1", p1.VendorId);
        Assert.Equal(1000, p1.WeightGrams);
        Assert.Equal("Medium-Dark", p1.Roast);

        Assert.Single(p1.Variants);
        var v1 = p1.Variants[0];
        Assert.Equal(1100m, v1.Price);
        Assert.Equal(1250m, v1.CompareAtPrice);
        Assert.True(v1.InStock);
        Assert.Equal("EGP", v1.Currency);

        // Product 2: Nespresso (no discount beforeDiscount == price, out of stock)
        var p2 = products[1];
        Assert.Equal("prod-uuid-2", p2.ExternalId);
        Assert.Equal("Nespresso Indonesia - 10 capsules", p2.Title);
        Assert.Equal("Capsules", p2.ProductType);
        Assert.Null(p2.WeightGrams); // weight 0 mapped to null

        Assert.Single(p2.Variants);
        var v2 = p2.Variants[0];
        Assert.Equal(699m, v2.Price);
        Assert.Null(v2.CompareAtPrice); // beforeDiscount == price -> no discount
        Assert.False(v2.InStock); // stock == 0

        // Product 3: Hario (from Accessories, company_name fallback)
        var p3 = products[2];
        Assert.Equal("prod-uuid-3", p3.ExternalId);
        Assert.Equal("Hario", p3.Vendor);
        Assert.Equal("Accessories", p3.Category);
        Assert.Equal("Drippers", p3.ProductType);
        Assert.Equal(200, p3.WeightGrams);
    }

    [Fact]
    public async Task FetchAsync_WhenPageCapExceeded_MarksDegraded()
    {
        using var _ = CollectionRunContext.Begin(Guid.NewGuid());

        var http = new FakeCollectorHttpClient()
            .When("/categories", CategoriesJson)
            .When("category=cat-coffee-123", CoffeePage1Json);

        // MaxPages = 0 causes page cap to trigger immediately on page 1
        var adapter = new LazzyBeanAdapter(http, timeout: TimeSpan.FromSeconds(30), maxPages: 0);

        var products = new List<RawProduct>();
        await foreach (var product in adapter.FetchAsync(Competitor, CancellationToken.None))
        {
            products.Add(product);
        }

        Assert.Empty(products);
        Assert.True(CollectionRunContext.IsDegraded);
        Assert.Contains("Page cap", CollectionRunContext.DegradedReason ?? "");
    }
}
