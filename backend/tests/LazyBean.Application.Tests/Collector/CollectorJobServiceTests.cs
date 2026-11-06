using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Collector;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Collector;

// Job lifecycle for the desktop collector: ordering inside a run, claims and leases, the no-claim
// skip, idempotent completion and the server Google fallback. Each service call runs against a
// cleared change tracker, the same way a fresh request scope would.
public class CollectorJobServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingFallback : ICollectorServerFallback
    {
        public List<Guid> Scheduled { get; } = [];

        public void ScheduleGoogleFallback(Guid jobId) => Scheduled.Add(jobId);
    }

    private sealed class Harness(LazyBeanDbContext db, MutableClock clock)
    {
        public LazyBeanDbContext Db { get; } = db;

        public MutableClock Clock { get; } = clock;

        public RecordingFallback Fallback { get; } = new();

        public CollectorJobService Jobs => new(
            new UnitOfWork(Db),
            Db,
            Fallback,
            new Mock<IAlertService>().Object,
            Clock,
            NullLogger<CollectorJobService>.Instance);
    }

    private static Harness NewHarness(DateTimeOffset? now = null)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Harness(new LazyBeanDbContext(options), new MutableClock(now ?? T0));
    }

    private static async Task<CollectorDevice> AddConnectedDeviceAsync(LazyBeanDbContext db, DateTimeOffset lastSeen)
    {
        var device = new CollectorDevice
        {
            Name = "Office PC",
            TokenHash = Guid.NewGuid().ToString("N"),
            Scopes = "ads:ingest store:ingest",
            PairedByUsername = "admin",
            LastSeenAt = lastSeen,
        };
        db.CollectorDevices.Add(device);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return device;
    }

    private static async Task<Competitor> AddStrongCoffeeAsync(LazyBeanDbContext db)
    {
        var competitor = new Competitor
        {
            Key = "strong-coffee",
            Name = "Strong Coffee",
            BaseUrl = "https://strongcoffee-eg.com",
            Platform = CompetitorPlatform.WooCommerce,
            Enabled = true,
            CollectionHost = "desktop",
        };
        db.Competitors.Add(competitor);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return competitor;
    }

    private static CollectorStoreTarget Target(Competitor competitor) => new(competitor.Id, competitor.Key);

    [Fact]
    public async Task PlanRun_WithConnectedApp_QueuesStoreThenMetaThenGoogle_AndIsIdempotent()
    {
        var h = NewHarness();
        await using var db = h.Db;
        await AddConnectedDeviceAsync(db, T0);
        var competitor = await AddStrongCoffeeAsync(db);
        var runId = Guid.NewGuid();

        var first = await h.Jobs.PlanRunAsync(runId, [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();
        var second = await h.Jobs.PlanRunAsync(runId, [Target(competitor)], CancellationToken.None);

        var jobs = await db.CollectorJobs.OrderBy(j => j.Sequence).ToListAsync();
        Assert.Equal(
            new[] { CollectorJobKind.StoreCollection, CollectorJobKind.MetaAds, CollectorJobKind.GoogleAds },
            jobs.Select(j => j.Kind).ToArray());
        Assert.All(jobs, j => Assert.Equal(CollectorJob.DesktopSource, j.Source));
        Assert.False(first.GoogleOnServer);
        Assert.Equal(first.GoogleJobId, second.GoogleJobId);
        Assert.Empty(h.Fallback.Scheduled);
    }

    [Fact]
    public async Task PlanRun_WithoutApp_RunsGoogleOnServer_AndSchedulesTheFallback()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var competitor = await AddStrongCoffeeAsync(db);

        var plan = await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);

        Assert.True(plan.GoogleOnServer);
        var google = await db.CollectorJobs.SingleAsync(j => j.Kind == CollectorJobKind.GoogleAds);
        Assert.Equal(CollectorJob.ServerSource, google.Source);
        Assert.Equal(CollectorJobStatus.Claimed, google.Status);
        Assert.Equal(new[] { google.Id }, h.Fallback.Scheduled);
    }

    [Fact]
    public async Task ClaimNext_HandsOutStoreFirst_AndMetaOnlyAfterStoreCompletes()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var device = await AddConnectedDeviceAsync(db, T0);
        var competitor = await AddStrongCoffeeAsync(db);
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();

        var store = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.NotNull(store);
        Assert.Equal("StoreCollection", store!.Kind);
        Assert.Equal("strong-coffee", store.Competitor!.Slug);
        Assert.Equal("Strong Coffee", store.Competitor.Name);

        var repoll = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal(store.Id, repoll!.Id);

        var result = await h.Jobs.CompleteAsync(device.Id, store.Id, new CompleteCollectorJobRequest("Completed", "done", null), CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal(CollectorCompletionResult.Completed, result);

        var next = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        Assert.Equal("MetaAds", next!.Kind);
    }

    [Fact]
    public async Task Heartbeat_KeepsAClaimAlive_PastTheLeaseWindow()
    {
        var clock = new MutableClock(T0);
        var h = new Harness(NewDbWithPlan(out var device, out var competitor), clock);
        await using var db = h.Db;
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();

        var store = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();

        clock.Now = T0.AddMinutes(4);
        Assert.True(await h.Jobs.HeartbeatAsync(device.Id, store!.Id, CancellationToken.None));
        db.ChangeTracker.Clear();

        clock.Now = T0.AddMinutes(8);
        await h.Jobs.MaintainAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(CollectorJobStatus.Claimed, (await db.CollectorJobs.SingleAsync(j => j.Id == store.Id)).Status);
    }

    [Fact]
    public async Task Complete_IsIdempotent_AndRejectsAnotherDevice()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var device = await AddConnectedDeviceAsync(db, T0);
        var other = await AddConnectedDeviceAsync(db, T0);
        var competitor = await AddStrongCoffeeAsync(db);
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();
        var store = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        var request = new CompleteCollectorJobRequest("Completed", "done", null);

        Assert.Equal(CollectorCompletionResult.Completed,
            await h.Jobs.CompleteAsync(device.Id, store!.Id, request, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(CollectorCompletionResult.AlreadyCompleted,
            await h.Jobs.CompleteAsync(device.Id, store.Id, request, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(CollectorCompletionResult.NotClaimedByDevice,
            await h.Jobs.CompleteAsync(other.Id, store.Id, request, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(CollectorCompletionResult.NotFound,
            await h.Jobs.CompleteAsync(device.Id, Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task Maintain_SkipsUnclaimedStoreAfterTheClaimWindow_WithoutTouchingTheRunsAdsJobs()
    {
        var clock = new MutableClock(T0);
        var h = new Harness(NewDbWithPlan(out _, out var competitor, connected: false), clock);
        await using var db = h.Db;
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();

        clock.Now = T0.AddMinutes(11);
        await h.Jobs.MaintainAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var store = await db.CollectorJobs.SingleAsync(j => j.Kind == CollectorJobKind.StoreCollection);
        Assert.Equal(CollectorJobStatus.Skipped, store.Status);
        Assert.Equal(CollectorJobService.SkippedNotRunning, store.Summary);

        // Meta was waiting behind the store job. It now starts its own claim window instead of being skipped.
        var meta = await db.CollectorJobs.SingleAsync(j => j.Kind == CollectorJobKind.MetaAds);
        Assert.Equal(CollectorJobStatus.Queued, meta.Status);
        Assert.Equal(clock.Now, meta.QueuedAt);
    }

    [Fact]
    public async Task Maintain_ReleasesAnAbandonedClaim_UntilItsAttemptsRunOut()
    {
        var clock = new MutableClock(T0);
        var h = new Harness(NewDbWithPlan(out var device, out var competitor), clock);
        await using var db = h.Db;
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();

        for (var attempt = 1; attempt <= CollectorJob.MaxAttempts; attempt++)
        {
            var claimed = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.NotNull(claimed);

            clock.Now = clock.Now.AddMinutes(6);
            await h.Jobs.MaintainAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }

        var store = await db.CollectorJobs.SingleAsync(j => j.Kind == CollectorJobKind.StoreCollection);
        Assert.Equal(CollectorJobStatus.Failed, store.Status);
        Assert.Equal(CollectorJob.MaxAttempts, store.Attempts);
    }

    [Fact]
    public async Task AdsOnly_PlansMetaThenGoogle_AndTheAppClaimsMetaFirst()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var device = await AddConnectedDeviceAsync(db, T0);

        var plan = await h.Jobs.PlanAdsOnlyAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.False(plan.GoogleOnServer);
        var claimed = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        Assert.Equal("MetaAds", claimed!.Kind);
        Assert.Null(claimed.RunId);
    }

    [Fact]
    public async Task Status_ReportsConnection_AndTheActiveGroupProgress()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var device = await AddConnectedDeviceAsync(db, T0);
        var competitor = await AddStrongCoffeeAsync(db);
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();
        await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();

        var status = await h.Jobs.GetStatusAsync(CancellationToken.None);

        Assert.True(status.Connected);
        Assert.Equal(1, status.ConnectedDeviceCount);
        Assert.Equal("StoreCollection", status.ActiveJob!.Kind);
        Assert.Equal("Office PC", status.ActiveJob.DeviceName);
        Assert.Equal(0, status.ActiveGroupCompleted);
        Assert.Equal(3, status.ActiveGroupTotal);
    }

    [Fact]
    public async Task Release_ReturnsTheClaimToTheQueue_WithoutCountingAnAttempt_AndRefusesAnotherDevice()
    {
        var clock = new MutableClock(T0);
        var h = new Harness(NewDbWithPlan(out var device, out var competitor), clock);
        await using var db = h.Db;
        var otherPc = await AddConnectedDeviceAsync(db, T0);
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();
        var store = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();

        clock.Now = T0.AddMinutes(1);
        Assert.False(await h.Jobs.ReleaseAsync(otherPc.Id, store!.Id, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.True(await h.Jobs.ReleaseAsync(device.Id, store.Id, CancellationToken.None));
        db.ChangeTracker.Clear();

        var job = await db.CollectorJobs.SingleAsync(j => j.Id == store.Id);
        Assert.Equal(CollectorJobStatus.Queued, job.Status);
        Assert.Null(job.ClaimedByDeviceId);
        Assert.Equal(0, job.Attempts);
        Assert.Equal(clock.Now, job.QueuedAt);
        db.ChangeTracker.Clear();

        // The same PC's next poll gets the job back.
        var again = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        Assert.Equal(store.Id, again!.Id);
    }

    [Fact]
    public async Task ClaimNext_ReportsTheJobsPlaceInItsBatch()
    {
        var h = NewHarness();
        await using var db = h.Db;
        var device = await AddConnectedDeviceAsync(db, T0);
        var competitor = await AddStrongCoffeeAsync(db);
        await h.Jobs.PlanRunAsync(Guid.NewGuid(), [Target(competitor)], CancellationToken.None);
        db.ChangeTracker.Clear();

        var store = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal((1, 3), (store!.BatchPosition, store.BatchTotal));

        await h.Jobs.CompleteAsync(device.Id, store.Id, new CompleteCollectorJobRequest("Completed", "done", null), CancellationToken.None);
        db.ChangeTracker.Clear();

        var meta = await h.Jobs.ClaimNextAsync(device.Id, CancellationToken.None);
        Assert.Equal("MetaAds", meta!.Kind);
        Assert.Equal((2, 3), (meta.BatchPosition, meta.BatchTotal));
    }

    private static LazyBeanDbContext NewDbWithPlan(out CollectorDevice device, out Competitor competitor, bool connected = true)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LazyBeanDbContext(options);
        device = connected
            ? AddConnectedDeviceAsync(db, T0).GetAwaiter().GetResult()
            : new CollectorDevice { Name = "unused", TokenHash = "unused", Scopes = "ads:ingest" };
        competitor = AddStrongCoffeeAsync(db).GetAwaiter().GetResult();
        return db;
    }

    private static LazyBeanDbContext NewDbWithPlan(out CollectorDevice device, out Competitor competitor)
    {
        return NewDbWithPlan(out device, out competitor, connected: true);
    }
}
