using System.Net;
using System.Net.Http.Json;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LazyBean.IntegrationTests;

[Collection("Database")]
public class InternalIngestionTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token-secret-1234";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = testDb.ConnectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Internal:Token"] = InternalToken,
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task IngestBatch_WithoutToken_ReturnsUnauthorized()
    {
        var runId = Guid.NewGuid();
        var payload = new IngestProductsBatchRequest(1, 1, []);

        var resp = await _client.PostAsJsonAsync($"/api/internal/runs/{runId}/competitors/strong-coffee/products", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task IngestBatch_WithWrongToken_ReturnsUnauthorized()
    {
        var runId = Guid.NewGuid();
        var payload = new IngestProductsBatchRequest(1, 1, []);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/runs/{runId}/competitors/strong-coffee/products");
        req.Headers.Add("X-Internal-Token", "wrong-token");
        req.Content = JsonContent.Create(payload);

        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task IngestBatch_And_Complete_AppliesGuardsAndIdempotency()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        var compSlug = "ingest-test-" + Guid.NewGuid().ToString("N")[..8];
        Guid competitorId;
        var runId = Guid.NewGuid();

        // 1. Seed competitor and run in DB
        await using (var db = new LazyBeanDbContext(options))
        {
            var comp = new Competitor
            {
                Key = compSlug,
                Name = "Ingest Test Comp",
                BaseUrl = "https://example.com",
                Platform = CompetitorPlatform.WooCommerce,
                MinDelayMs = 1000,
                CollectionHost = "github",
                Enabled = true
            };
            db.Competitors.Add(comp);

            var run = new CollectionRun
            {
                Id = runId,
                Trigger = RunTrigger.Scheduled,
                Status = CollectionRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.CollectionRuns.Add(run);
            await db.SaveChangesAsync();
            competitorId = comp.Id;
        }

        // 2. Ingest first batch of 2 products
        var rawProducts = new List<RawProduct>
        {
            new(
                ExternalId: "ext-1",
                Title: "Espresso Roast Coffee 250g",
                CanonicalUrl: "https://example.com/p1",
                Category: "Coffee",
                Variants:
                [
                    new RawVariant(
                        ExternalId: "var-1",
                        Title: "250g",
                        Price: 200m,
                        CompareAtPrice: 250m,
                        Currency: "EGP",
                        InStock: true,
                        Sku: "BX-250")
                ],
                ProductType: "Whole Bean",
                Vendor: "BrandX",
                Tags: "espresso, dark",
                VendorId: null,
                WeightGrams: 250,
                Handle: "espresso-roast-250g"
            ),
            new(
                ExternalId: "ext-2",
                Title: "Colombian Single Origin 500g",
                CanonicalUrl: "https://example.com/p2",
                Category: "Coffee",
                Variants:
                [
                    new RawVariant(
                        ExternalId: "var-2",
                        Title: "500g",
                        Price: 380m,
                        CompareAtPrice: null,
                        Currency: "EGP",
                        InStock: true,
                        Sku: "BX-500")
                ],
                ProductType: "Ground",
                Vendor: "BrandX",
                Tags: "single-origin",
                VendorId: null,
                WeightGrams: 500,
                Handle: "colombian-500g"
            )
        };

        var batchPayload = new IngestProductsBatchRequest(1, 1, rawProducts);
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/runs/{runId}/competitors/{compSlug}/products"))
        {
            req.Headers.Add("X-Internal-Token", InternalToken);
            req.Content = JsonContent.Create(batchPayload);

            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var batchResult = await resp.Content.ReadFromJsonAsync<IngestBatchResult>();
            Assert.NotNull(batchResult);
            Assert.Equal(2, batchResult.ProductsSaved);
            Assert.Equal(2, batchResult.VariantsSaved);
            Assert.Equal(2, batchResult.PriceObservationsRecorded);
        }

        // 3. Verify Database State
        List<Guid> savedProductIds;
        await using (var db = new LazyBeanDbContext(options))
        {
            var savedProducts = await db.Products.Where(p => p.CompetitorId == competitorId).ToListAsync();
            Assert.Equal(2, savedProducts.Count);
            savedProductIds = savedProducts.Select(p => p.Id).ToList();

            var savedVariants = await db.Variants.Where(v => savedProductIds.Contains(v.ProductId)).ToListAsync();
            Assert.Equal(2, savedVariants.Count);

            var savedObs = await db.PriceObservations.Where(o => o.RunId == runId).ToListAsync();
            Assert.Equal(2, savedObs.Count);
        }

        // 4. Test Idempotency: Send same batch again -> 0 new products, 0 new variants, 0 new price observations
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/runs/{runId}/competitors/{compSlug}/products"))
        {
            req.Headers.Add("X-Internal-Token", InternalToken);
            req.Content = JsonContent.Create(batchPayload);

            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var batchResult = await resp.Content.ReadFromJsonAsync<IngestBatchResult>();
            Assert.NotNull(batchResult);
            Assert.Equal(0, batchResult.ProductsSaved);
            Assert.Equal(0, batchResult.VariantsSaved);
            Assert.Equal(0, batchResult.PriceObservationsRecorded);
        }

        // 5. Test Zero-Products Guard: If external collector reports 0 products, marks Failed without removing items
        var zeroPayload = new CompleteCompetitorRunRequest("Completed", 1, 0, null);
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/runs/{runId}/competitors/{compSlug}/complete"))
        {
            req.Headers.Add("X-Internal-Token", InternalToken);
            req.Content = JsonContent.Create(zeroPayload);

            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var completeRes = await resp.Content.ReadFromJsonAsync<CompleteCompetitorResult>();
            Assert.NotNull(completeRes);
            Assert.Equal("Failed", completeRes.Status);
            Assert.Contains("0 products reported", completeRes.Message);
        }

        // Confirm active products were NOT removed
        await using (var db = new LazyBeanDbContext(options))
        {
            var active = await db.Products.Where(p => p.CompetitorId == competitorId && p.Status == ProductStatus.Active).ToListAsync();
            Assert.Equal(2, active.Count);
        }

        // 6. Test Healthy Completion:
        var healthyPayload = new CompleteCompetitorRunRequest("Completed", 1, 2, null);
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/runs/{runId}/competitors/{compSlug}/complete"))
        {
            req.Headers.Add("X-Internal-Token", InternalToken);
            req.Content = JsonContent.Create(healthyPayload);

            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var completeRes = await resp.Content.ReadFromJsonAsync<CompleteCompetitorResult>();
            Assert.NotNull(completeRes);
            Assert.Equal("Completed", completeRes.Status);
        }
    }
}
