using System.Net;
using System.Net.Http.Json;
using LazyBean.Application.Matching;
using LazyBean.Application.Matching.Commands.CreateMatch;
using LazyBean.Application.Matching.Commands.SubmitMatchDecision;
using LazyBean.Application.Matching.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LazyBean.IntegrationTests.Matching;

[Collection("Database")]
public class MatchingIntegrationTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-matching-user";
    private const string Password = "P@ssw0rd-Matching-Tests!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        if (existing is null)
        {
            db.Users.Add(new User { Username = Username, PasswordHash = new PasswordHasherAdapter().Hash(Password) });
            await db.SaveChangesAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = testDb.ConnectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Admin:Username"] = "",
                    ["Admin:Password"] = ""
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost")
        });

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        loginRes.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task MatchingEngineAndEndpoints_EndToEndFlow()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE competitors, products, variants, price_observations, collection_runs, " +
            "competitor_runs, change_events, review_items, fetched_resources, product_matches CASCADE;");

        // Reference competitor
        var refComp = new Competitor
        {
            Key = $"it-ref-{Guid.NewGuid():N}",
            Name = "Lazy Bean Reference",
            BaseUrl = "https://api.lazzybean.com",
            Platform = CompetitorPlatform.LazzyBeanApi,
            IsReference = true,
            Enabled = true
        };
        // Target competitor
        var targetComp = new Competitor
        {
            Key = $"it-target-{Guid.NewGuid():N}",
            Name = "Competitor Store",
            BaseUrl = "https://competitor.example.com",
            Platform = CompetitorPlatform.Shopify,
            IsReference = false,
            Enabled = true
        };
        db.Competitors.AddRange(refComp, targetComp);
        await db.SaveChangesAsync();

        // High-similarity product (Auto-accept)
        var refProduct1 = new Product
        {
            CompetitorId = refComp.Id,
            ExternalId = "ref-1",
            Title = "Lavazza Crema e Gusto Ground Coffee 250g",
            NormalizedTitle = "lavazza crema e gusto ground coffee 250g",
            CanonicalUrl = "https://lazzybean.com/single-product/ref-1",
            Vendor = "Lavazza",
            CanonicalCategory = CanonicalCategory.GroundCoffee,
            WeightGrams = 250,
            Status = ProductStatus.Active
        };
        var compProduct1 = new Product
        {
            CompetitorId = targetComp.Id,
            ExternalId = "comp-1",
            Title = "Lavazza Crema e Gusto Ground Coffee 250g",
            NormalizedTitle = "lavazza crema e gusto ground coffee 250g",
            CanonicalUrl = "https://competitor.example.com/products/comp-1",
            Vendor = "Lavazza",
            CanonicalCategory = CanonicalCategory.GroundCoffee,
            WeightGrams = 250,
            Status = ProductStatus.Active
        };

        // Moderate-similarity product (Review item)
        var refProduct2 = new Product
        {
            CompetitorId = refComp.Id,
            ExternalId = "ref-2",
            Title = "Illy Espresso Decaffeinato Ground Coffee 250g",
            NormalizedTitle = "illy espresso decaffeinato ground coffee 250g",
            CanonicalUrl = "https://lazzybean.com/single-product/ref-2",
            Vendor = "Illy",
            CanonicalCategory = CanonicalCategory.GroundCoffee,
            WeightGrams = 250,
            Status = ProductStatus.Active
        };
        var compProduct2 = new Product
        {
            CompetitorId = targetComp.Id,
            ExternalId = "comp-2",
            Title = "Illy Ground Decaf Coffee 250g Blue",
            NormalizedTitle = "illy ground decaf coffee 250g blue",
            CanonicalUrl = "https://competitor.example.com/products/comp-2",
            Vendor = "Illy",
            CanonicalCategory = CanonicalCategory.GroundCoffee,
            WeightGrams = 250,
            Status = ProductStatus.Active
        };

        db.Products.AddRange(refProduct1, compProduct1, refProduct2, compProduct2);
        await db.SaveChangesAsync();

        var refVar1 = new Variant { ProductId = refProduct1.Id, ExternalId = "ref-v1" };
        var compVar1 = new Variant { ProductId = compProduct1.Id, ExternalId = "comp-v1" };
        var refVar2 = new Variant { ProductId = refProduct2.Id, ExternalId = "ref-v2" };
        var compVar2 = new Variant { ProductId = compProduct2.Id, ExternalId = "comp-v2" };
        db.Variants.AddRange(refVar1, compVar1, refVar2, compVar2);
        await db.SaveChangesAsync();

        // Run matching via API endpoint: POST /api/matches/run
        var matchRunRes = await _client.PostAsync("/api/matches/run", null);
        Assert.Equal(HttpStatusCode.OK, matchRunRes.StatusCode);
        var matchRunBody = await matchRunRes.Content.ReadFromJsonAsync<MatchingResult>();
        Assert.NotNull(matchRunBody);
        Assert.True(matchRunBody.AutoAcceptedCount >= 1);

        // Verify auto-accepted match exists in DB
        var autoMatch = await db.ProductMatches.FirstOrDefaultAsync(
            m => m.CompetitorVariantId == compVar1.Id && m.ReferenceVariantId == refVar1.Id);
        Assert.NotNull(autoMatch);
        Assert.Equal(MatchStatus.Auto, autoMatch.Status);
        Assert.True(autoMatch.Score >= 0.85m);

        // Verify review item via GET /api/reviews
        var reviewsRes = await _client.GetAsync("/api/reviews?status=Pending");
        Assert.Equal(HttpStatusCode.OK, reviewsRes.StatusCode);
        var reviewsBody = await reviewsRes.Content.ReadFromJsonAsync<ReviewQueueResponseDto>();
        Assert.NotNull(reviewsBody);
        Assert.NotEmpty(reviewsBody.Matches);

        var targetReview = reviewsBody.Matches.FirstOrDefault(m => m.CompetitorProduct?.ProductId == compProduct2.Id);
        Assert.NotNull(targetReview);
        Assert.True(targetReview.Score >= 0.65m && targetReview.Score < 0.85m);

        // Submit decision: POST /api/matches/{id}/decision with "approve"
        var decisionRes = await _client.PostAsJsonAsync(
            $"/api/matches/{targetReview.Id}/decision",
            new { decision = "approve", notes = "Spot check approved" });
        Assert.Equal(HttpStatusCode.OK, decisionRes.StatusCode);

        var approvedMatch = await db.ProductMatches.FirstOrDefaultAsync(
            m => m.CompetitorVariantId == compVar2.Id && m.ReferenceVariantId == refVar2.Id);
        Assert.NotNull(approvedMatch);
        Assert.Equal(MatchStatus.Approved, approvedMatch.Status);
        Assert.Equal(MatchMethod.Manual, approvedMatch.Method);

        // Test manual match creation: POST /api/matches
        var manualRes = await _client.PostAsJsonAsync("/api/matches", new CreateMatchCommand(
            CompetitorVariantId: compVar1.Id,
            ReferenceVariantId: refVar2.Id,
            Approve: true,
            Notes: "Manual override link"));
        Assert.Equal(HttpStatusCode.OK, manualRes.StatusCode);
        var manualBody = await manualRes.Content.ReadFromJsonAsync<MatchResultDto>();
        Assert.NotNull(manualBody);
        Assert.Equal("approved", manualBody.Status);
    }
}
