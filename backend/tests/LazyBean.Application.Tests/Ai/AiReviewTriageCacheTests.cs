using System.Text.Json;
using System.Text.Json.Nodes;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ai;

public class AiReviewTriageCacheTests
{
    private LazyBeanDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new LazyBeanDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task TriagePendingReviewsAsync_WhenCached_UsesCacheWithoutCallingAiClient()
    {
        using var db = CreateInMemoryDbContext();

        // 1. Enable AI
        db.AiSettings.Add(new AiSetting
        {
            IsEnabled = true,
            EncryptedApiKey = "dummy-encrypted-key",
            ModelName = "gemini-3.8-flash",
            DailyRequestCap = 500
        });

        // 2. Mock AI Client
        var mockAiClient = new Mock<IAiClient>();

        var service = new AiReviewTriageService(db, mockAiClient.Object, NullLogger<AiReviewTriageService>.Instance);

        var refTitle = "Caffè Vergnano Antica Bottega Whole Bean Coffee - 500g";
        var compTitle = "Caffè Vergnano - Antica Bottega Whole Beans - 500g";
        var pairHash = service.ComputePairHash(
            refTitle, "Caffè Vergnano", "Coffee", 500, null, null,
            compTitle, "Caffè Vergnano", "Coffee", 500, null, null);

        // 3. Pre-seed cache with verdict "same"
        db.AiReviewTriageCaches.Add(new AiReviewTriageCache
        {
            PairHash = pairHash,
            Verdict = "same",
            Confidence = 0.98m,
            Reason = "Exact brand, line, and 500g weight match.",
            ModelName = "gemini-3.8-flash",
            AnalyzedAt = DateTimeOffset.UtcNow
        });

        // 4. Seed pending review item with this pair
        var payload = new JsonObject
        {
            ["ReferenceTitle"] = refTitle,
            ["ReferenceBrand"] = "Caffè Vergnano",
            ["ReferenceCategory"] = "Coffee",
            ["ReferenceWeightGrams"] = 500,
            ["CompetitorTitle"] = compTitle,
            ["CompetitorBrand"] = "Caffè Vergnano",
            ["CompetitorCategory"] = "Coffee",
            ["CompetitorWeightGrams"] = 500
        };

        var reviewItem = new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Pending,
            PayloadJson = payload.ToJsonString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewItems.Add(reviewItem);
        await db.SaveChangesAsync();

        // 5. Run triage
        var stats = await service.TriagePendingReviewsAsync(10);

        // Verify: 1 processed, 1 cache hit, 0 API calls
        Assert.Equal(1, stats.TotalEvaluated);
        Assert.Equal(1, stats.CacheHits);
        Assert.Equal(0, stats.ApiCallsMade);
        Assert.Equal(1, stats.SameCount);

        // AI client must NEVER have been called
        mockAiClient.Verify(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()), Times.Never);

        // ReviewItem payload must have AI fields applied
        var updatedNode = JsonNode.Parse(reviewItem.PayloadJson);
        Assert.NotNull(updatedNode);
        Assert.Equal("same", updatedNode["AiVerdict"]?.GetValue<string>());
        Assert.Equal(0.98m, updatedNode["AiConfidence"]?.GetValue<decimal>());
        Assert.Equal("Exact brand, line, and 500g weight match.", updatedNode["AiReason"]?.GetValue<string>());
        Assert.Equal(pairHash, updatedNode["AiPairHash"]?.GetValue<string>());
    }

    [Fact]
    public async Task TriagePendingReviewsAsync_WhenUncached_CallsAiClientAndStoresInCache()
    {
        using var db = CreateInMemoryDbContext();

        // 1. Enable AI
        db.AiSettings.Add(new AiSetting
        {
            IsEnabled = true,
            EncryptedApiKey = "dummy-encrypted-key",
            ModelName = "gemini-3.8-flash",
            DailyRequestCap = 500
        });

        var refTitle = "Caffè Vergnano Espresso Pods - 15 Pods";
        var compTitle = "Caffè Vergnano Espresso Pods - 150 Pods";

        var payload = new JsonObject
        {
            ["ReferenceTitle"] = refTitle,
            ["ReferenceBrand"] = "Caffè Vergnano",
            ["CompetitorTitle"] = compTitle,
            ["CompetitorBrand"] = "Caffè Vergnano"
        };

        var reviewItem = new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Pending,
            PayloadJson = payload.ToJsonString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewItems.Add(reviewItem);
        await db.SaveChangesAsync();

        var mockAiClient = new Mock<IAiClient>();
        mockAiClient.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiReviewTriagePairRequest> reqs, CancellationToken ct) =>
            {
                return reqs.Select(r => new AiReviewTriageResult(
                    r.ReviewId,
                    r.PairHash,
                    "different",
                    0.95m,
                    "15 pods vs 150 pods pack size discrepancy."
                )).ToList();
            });

        var service = new AiReviewTriageService(db, mockAiClient.Object, NullLogger<AiReviewTriageService>.Instance);

        // Run triage
        var stats = await service.TriagePendingReviewsAsync(10);

        Assert.Equal(1, stats.TotalEvaluated);
        Assert.Equal(0, stats.CacheHits);
        Assert.Equal(1, stats.ApiCallsMade);
        Assert.Equal(1, stats.DifferentCount);

        // Verify API client was called once
        mockAiClient.Verify(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify stored in cache table
        var expectedHash = service.ComputePairHash(
            refTitle, "Caffè Vergnano", null, null, null, null,
            compTitle, "Caffè Vergnano", null, null, null, null);

        var cached = await db.AiReviewTriageCaches.FirstOrDefaultAsync(c => c.PairHash == expectedHash);
        Assert.NotNull(cached);
        Assert.Equal("different", cached.Verdict);
        Assert.Equal(0.95m, cached.Confidence);
        Assert.Equal("15 pods vs 150 pods pack size discrepancy.", cached.Reason);

        // Verify review item payload updated
        var updatedNode = JsonNode.Parse(reviewItem.PayloadJson);
        Assert.NotNull(updatedNode);
        Assert.Equal("different", updatedNode["AiVerdict"]?.GetValue<string>());
        Assert.Equal(0.95m, updatedNode["AiConfidence"]?.GetValue<decimal>());
    }
}
