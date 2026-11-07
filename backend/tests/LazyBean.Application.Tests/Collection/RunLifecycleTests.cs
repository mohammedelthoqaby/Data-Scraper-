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

// How a run finishes. A run is Completed only with its phases on record. When a desktop app is connected, the
// run waits for the app's Meta and Google jobs. When no app is connected, or it disconnects for five minutes,
// the unfinished ads jobs are skipped and the run finishes without them.
public class RunLifecycleTests
{
    [Fact]
    public async Task Completed_RunAlwaysHasItsPhasesOnRecord()
    {
        var factory = NewFactory();
        var runId = await SeedRunAsync(factory);

        // No post-processing scope is configured here, so the phases are recorded as skipped rather than missing.
        await CreateExecutor(factory, jobs: null).ExecuteAsync(runId, CancellationToken.None);

        var run = await ReadRunAsync(factory, runId);
        Assert.Equal(CollectionRunStatus.Completed, run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.PhasesJson), "A Completed run must have its post-processing phases recorded.");
        Assert.Contains("\"name\":\"classification\"", run.PhasesJson);
        Assert.Contains("\"name\":\"alerts\"", run.PhasesJson);
    }

    [Fact]
    public async Task AppConnected_RunWaitsForTheMetaJob_AndRecordsItsAdsPhase()
    {
        var factory = NewFactory();
        var runId = await SeedRunAsync(factory);
        var calls = 0;
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetRunJobsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => calls++ == 0
                ? Jobs(Job("MetaAds", "Claimed", label: "Meta page 4/5: Fengany", done: 4, total: 5))
                : Jobs(Job("MetaAds", "Completed")));

        await CreateExecutor(factory, jobs.Object).ExecuteAsync(runId, CancellationToken.None);

        var run = await ReadRunAsync(factory, runId);
        Assert.Equal(CollectionRunStatus.Completed, run.Status);
        Assert.Contains("\"name\":\"ads\",\"status\":\"completed\"", run.PhasesJson);
        jobs.Verify(j => j.GetRunJobsAsync(runId, It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task NoApp_RunDoesNotWaitForItsAdsJobs_AndFinishesPartial()
    {
        var factory = NewFactory();
        var runId = await SeedRunAsync(factory);
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetRunJobsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Jobs(Job("MetaAds", "Skipped"), Job("GoogleAds", "Completed", source: "server")));

        var started = DateTimeOffset.UtcNow;
        await CreateExecutor(factory, jobs.Object).ExecuteAsync(runId, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - started;

        var run = await ReadRunAsync(factory, runId);
        // The Meta ads were not collected, so the run is partial rather than complete.
        Assert.Equal(CollectionRunStatus.Partial, run.Status);
        Assert.Contains("\"name\":\"ads\",\"status\":\"skipped\"", run.PhasesJson);
        // Nothing to wait for: the executor's three-second poll never runs.
        Assert.True(elapsed < TimeSpan.FromSeconds(2.5), $"The run waited {elapsed.TotalSeconds:0.0}s with no app connected.");
        jobs.Verify(j => j.SkipAdsJobsIfDisconnectedAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AppDisconnects_UnfinishedAdsJobsAreSkipped_AndTheRunStillFinishes()
    {
        var factory = NewFactory();
        var runId = await SeedRunAsync(factory);
        var skipped = false;
        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.GetRunJobsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => skipped
                ? Jobs(Job("MetaAds", "Skipped"))
                : Jobs(Job("MetaAds", "Claimed", label: "Meta page 2/5: Bella")));
        jobs.Setup(j => j.SkipAdsJobsIfDisconnectedAsync(runId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => skipped = true)
            .ReturnsAsync(1);

        await CreateExecutor(factory, jobs.Object).ExecuteAsync(runId, CancellationToken.None);

        var run = await ReadRunAsync(factory, runId);
        Assert.Equal(CollectionRunStatus.Partial, run.Status);
        Assert.Contains("\"name\":\"ads\",\"status\":\"skipped\"", run.PhasesJson);
        jobs.Verify(j => j.SkipAdsJobsIfDisconnectedAsync(runId, TimeSpan.FromMinutes(5), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IDbContextFactory<LazyBeanDbContext> NewFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LazyBeanDbContext>>();
    }

    private static async Task<Guid> SeedRunAsync(IDbContextFactory<LazyBeanDbContext> factory)
    {
        var run = new CollectionRun { Status = CollectionRunStatus.Queued };
        await using var db = await factory.CreateDbContextAsync();
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private static async Task<CollectionRun> ReadRunAsync(IDbContextFactory<LazyBeanDbContext> factory, Guid runId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CollectionRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
    }

    private static CollectionRunExecutor CreateExecutor(IDbContextFactory<LazyBeanDbContext> factory, ICollectorJobService? jobs) =>
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

    private static IReadOnlyList<CollectorJobSummaryDto> Jobs(params CollectorJobSummaryDto[] jobs) => jobs;

    private static CollectorJobSummaryDto Job(
        string kind,
        string status,
        string source = "desktop",
        string? label = null,
        int? done = null,
        int? total = null) =>
        new(Guid.NewGuid(), kind, source, status, null, null, DateTimeOffset.UtcNow, null, label, done, total);
}
