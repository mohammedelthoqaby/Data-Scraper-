using LazyBean.Application.Collection.Classification;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collection.Orchestration;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

// The executor's desktop wait loop: how a run ends when the LazyBean Collector app collects a competitor.
// The loop checks every 3 seconds, so each case takes a few seconds. The loop is private, so these tests
// go through ExecuteAsync with one desktop competitor.
public class DesktopWaitLoopTests
{
    [Fact]
    public async Task Skipped_WhenNoCollectorPicksUpTheJob_MarksTheCompetitorSkippedWithoutErrors()
    {
        var seeded = await SeedDesktopRunAsync();
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetStoreJobStateAsync(seeded.RunId, seeded.CompetitorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoreJob("Skipped", "Skipped (collector not running)", claimed: false));

        await CreateExecutor(seeded.Factory, jobs.Object).ExecuteAsync(seeded.RunId, CancellationToken.None);

        var (run, competitorRun) = await ReadOutcomeAsync(seeded);
        Assert.Equal(CompetitorRunStatus.Skipped, competitorRun.Status);
        Assert.Equal(0, competitorRun.Errors);
        Assert.Equal(RunCoverage.Partial, competitorRun.Coverage);
        // A skipped desktop competitor was not collected, so the run is partial, not complete.
        Assert.Equal(CollectionRunStatus.Partial, run.Status);
    }

    [Fact]
    public async Task Failed_WhenTheCollectorReportsAFailure_MarksTheCompetitorFailed()
    {
        var seeded = await SeedDesktopRunAsync();
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetStoreJobStateAsync(seeded.RunId, seeded.CompetitorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoreJob("Failed", "Store blocked the collector.", claimed: true));

        await CreateExecutor(seeded.Factory, jobs.Object).ExecuteAsync(seeded.RunId, CancellationToken.None);

        var (run, competitorRun) = await ReadOutcomeAsync(seeded);
        Assert.Equal(CompetitorRunStatus.Failed, competitorRun.Status);
        Assert.True(competitorRun.Errors >= 1);
        Assert.Equal(RunCoverage.Partial, competitorRun.Coverage);
        // The only competitor failed, so the run fails with it.
        Assert.Equal(CollectionRunStatus.Failed, run.Status);
    }

    [Fact]
    public async Task Completed_WhenTheAppFinishedButSavedNothing_MarksTheCompetitorFailed()
    {
        var seeded = await SeedDesktopRunAsync();
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetStoreJobStateAsync(seeded.RunId, seeded.CompetitorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoreJob("Completed", null, claimed: false));

        await CreateExecutor(seeded.Factory, jobs.Object).ExecuteAsync(seeded.RunId, CancellationToken.None);

        var (run, competitorRun) = await ReadOutcomeAsync(seeded);
        // The job says done, but the competitor was never saved, so the loop must not report success.
        Assert.Equal(CompetitorRunStatus.Failed, competitorRun.Status);
        Assert.True(competitorRun.Errors >= 1);
        Assert.Equal(CollectionRunStatus.Failed, run.Status);
    }

    [Fact]
    public async Task Finished_WhenTheCollectorAppCompletesTheCompetitor_KeepsItCompleted()
    {
        var seeded = await SeedDesktopRunAsync();
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetStoreJobStateAsync(seeded.RunId, seeded.CompetitorId, It.IsAny<CancellationToken>()))
            .Callback(() => MarkCompetitorCompletedLikeTheAppDoes(seeded))
            .ReturnsAsync(StoreJob("Claimed", null, claimed: true));

        await CreateExecutor(seeded.Factory, jobs.Object).ExecuteAsync(seeded.RunId, CancellationToken.None);

        var (run, competitorRun) = await ReadOutcomeAsync(seeded);
        Assert.Equal(CompetitorRunStatus.Completed, competitorRun.Status);
        Assert.Equal(12, competitorRun.Products);
        Assert.Equal(0, competitorRun.Errors);
        Assert.Equal(CollectionRunStatus.Completed, run.Status);
    }

    private sealed class DesktopRun
    {
        public required IDbContextFactory<LazyBeanDbContext> Factory { get; init; }
        public required Guid RunId { get; init; }
        public required Guid CompetitorId { get; init; }
    }

    private static async Task<DesktopRun> SeedDesktopRunAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();

        var competitor = new Competitor
        {
            Key = "strong-coffee",
            Name = "Strong Coffee",
            BaseUrl = "https://strong-coffee.example",
            Platform = CompetitorPlatform.Shopify,
            CollectionHost = "desktop",
        };
        var run = new CollectionRun { Status = CollectionRunStatus.Queued };

        await using var db = await factory.CreateDbContextAsync();
        db.Competitors.Add(competitor);
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync();

        return new DesktopRun { Factory = factory, RunId = run.Id, CompetitorId = competitor.Id };
    }

    private static CollectionRunExecutor CreateExecutor(IDbContextFactory<LazyBeanDbContext> factory, ICollectorJobService jobs) =>
        new(
            dbContextFactory: factory,
            adapters: Mock.Of<ICompetitorAdapterResolver>(),
            cache: Mock.Of<ICacheService>(),
            classifier: Mock.Of<IProductClassifier>(),
            http: null,
            configuration: null,
            googleAdsSyncService: null,
            serviceScopeFactory: null,
            progressTracker: null,
            collectorJobs: jobs,
            logger: NullLogger<CollectionRunExecutor>.Instance);

    private static CollectorStoreJobState StoreJob(string status, string? summary, bool claimed) =>
        new(Guid.NewGuid(), status, summary, DateTimeOffset.UtcNow, null, claimed, null);

    // Stands in for the collector app's /complete call, which finishes the competitor's run.
    private static void MarkCompetitorCompletedLikeTheAppDoes(DesktopRun seeded)
    {
        using var db = seeded.Factory.CreateDbContext();
        var competitorRun = db.CompetitorRuns.First(r => r.RunId == seeded.RunId && r.CompetitorId == seeded.CompetitorId);
        competitorRun.Status = CompetitorRunStatus.Completed;
        competitorRun.Products = 12;
        db.SaveChanges();
    }

    private static async Task<(CollectionRun Run, CompetitorRun CompetitorRun)> ReadOutcomeAsync(DesktopRun seeded)
    {
        await using var db = await seeded.Factory.CreateDbContextAsync();
        var run = await db.CollectionRuns.AsNoTracking().FirstAsync(r => r.Id == seeded.RunId);
        var competitorRun = await db.CompetitorRuns.AsNoTracking()
            .FirstAsync(r => r.RunId == seeded.RunId && r.CompetitorId == seeded.CompetitorId);
        return (run, competitorRun);
    }
}
