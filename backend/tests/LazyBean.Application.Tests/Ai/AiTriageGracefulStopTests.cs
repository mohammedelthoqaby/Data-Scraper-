using System.Text.Json.Nodes;
using LazyBean.Application.Ai;
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
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Ai;

/// <summary>Triage must never fail a run: quota stops leave items pending, and the per-run cap is configurable.</summary>
public class AiTriageGracefulStopTests
{
    private static async Task<LazyBeanDbContext> SeedAsync(int pendingCount, int triageRunCap = 200)
    {
        var db = CreateDb();
        db.AiSettings.Add(new AiSetting { IsEnabled = true, PrimaryProvider = "groq", DailyRequestCap = 1000, TriageRunCap = triageRunCap });

        for (var i = 0; i < pendingCount; i++)
        {
            var payload = new JsonObject
            {
                ["ReferenceTitle"] = $"Ref product {i}",
                ["ReferenceBrand"] = "Brand",
                ["CompetitorTitle"] = $"Competitor product {i}",
                ["CompetitorBrand"] = "Brand",
            };
            db.ReviewItems.Add(new ReviewItem
            {
                Kind = ReviewItemKind.ProductMatch,
                Status = ReviewStatus.Pending,
                PayloadJson = payload.ToJsonString(),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-pendingCount + i),
            });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task WhenQuotaIsReached_TriageStopsWithoutThrowingAndLeavesItemsPending()
    {
        using var db = await SeedAsync(pendingCount: 4);
        var ai = new Mock<IAiClient>();
        ai.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiUnavailableException("AI quota reached", quotaReached: true));

        var service = new AiReviewTriageService(db, ai.Object, NullLogger<AiReviewTriageService>.Instance);

        var stats = await service.TriagePendingReviewsAsync();

        Assert.Equal(4, stats.Deferred);
        Assert.Equal("AI quota reached", stats.StopReason);
        Assert.Equal(0, stats.SameCount + stats.DifferentCount + stats.UnsureCount);

        var stillPending = await db.ReviewItems.AsNoTracking().CountAsync(r => r.Status == ReviewStatus.Pending);
        Assert.Equal(4, stillPending);
    }

    [Fact]
    public async Task WhenProviderFailsUnexpectedly_TriageDoesNotFailTheRun()
    {
        using var db = await SeedAsync(pendingCount: 3);
        var ai = new Mock<IAiClient>();
        ai.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new AiReviewTriageService(db, ai.Object, NullLogger<AiReviewTriageService>.Instance);

        var stats = await service.TriagePendingReviewsAsync();

        Assert.Equal(3, stats.Deferred);
        Assert.Equal("AI triage error", stats.StopReason);
    }

    [Fact]
    public async Task PerRunCapIsConfigurable_AndCallersCannotExceedIt()
    {
        using var db = await SeedAsync(pendingCount: 6, triageRunCap: 3);
        var ai = new Mock<IAiClient>();
        ai.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiReviewTriagePairRequest> batch, CancellationToken _) =>
                batch.Select(p => new AiReviewTriageResult(p.ReviewId, p.PairHash, "different", 0.9m, "pack size")).ToList());

        var service = new AiReviewTriageService(db, ai.Object, NullLogger<AiReviewTriageService>.Instance);

        // Asking for 100 is capped to the configured 3.
        var stats = await service.TriagePendingReviewsAsync(100);

        Assert.Equal(3, stats.TotalEvaluated);
        Assert.Equal(0, stats.Deferred);
        Assert.Equal(3, stats.DifferentCount);
    }

    [Fact]
    public async Task DefaultRunCap_Is200()
    {
        using var db = await SeedAsync(pendingCount: 5);
        var ai = new Mock<IAiClient>();
        ai.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiReviewTriagePairRequest> batch, CancellationToken _) =>
                batch.Select(p => new AiReviewTriageResult(p.ReviewId, p.PairHash, "same", 0.9m, "match")).ToList());

        var service = new AiReviewTriageService(db, ai.Object, NullLogger<AiReviewTriageService>.Instance);

        var stats = await service.TriagePendingReviewsAsync();

        Assert.Equal(5, stats.TotalEvaluated);
    }
}
