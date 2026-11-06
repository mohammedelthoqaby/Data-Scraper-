using System.Text.Json;
using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collector;

// Work queue for paired collector apps. Writes go through the unit of work (tracked-free reads from
// FindAsync, then Update, so the xmin concurrency token guards every claim); reads for projections
// go through IReadDbContext.
public class CollectorJobService(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ICollectorServerFallback serverFallback,
    IAlertService alertService,
    TimeProvider clock,
    ILogger<CollectorJobService> logger) : ICollectorJobService
{
    // A job that no app claimed within this window (once it became available) is skipped.
    public static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(10);

    // A claimed job whose app has not heartbeated for this long goes back to the queue.
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    // An app is "connected" while it has polled within this window (it polls every 10 seconds).
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(45);

    // A server-run job that never reports back is failed after this long.
    public static readonly TimeSpan ServerJobTimeout = TimeSpan.FromMinutes(30);

    public const string SkippedNotRunning = "Skipped (collector not running)";

    public async Task<CollectorRunPlan> PlanRunAsync(Guid runId, IReadOnlyList<CollectorStoreTarget> stores, CancellationToken ct)
    {
        var existingGoogle = await readContext.Query<CollectorJob>()
            .Where(j => j.GroupId == runId && j.Kind == CollectorJobKind.GoogleAds)
            .Select(j => new { j.Id, j.Source })
            .FirstOrDefaultAsync(ct);
        if (existingGoogle is not null)
        {
            // Already planned (a resumed run): leave the existing jobs untouched.
            return new CollectorRunPlan(existingGoogle.Id, existingGoogle.Source == CollectorJob.ServerSource);
        }

        var now = clock.GetUtcNow();
        var connected = await IsCollectorConnectedAsync(ct);

        foreach (var store in stores)
        {
            await unitOfWork.CollectorJobs.AddAsync(new CollectorJob
            {
                GroupId = runId,
                RunId = runId,
                Kind = CollectorJobKind.StoreCollection,
                Sequence = 1,
                CompetitorId = store.CompetitorId,
                CompetitorSlug = store.Slug,
                QueuedAt = now,
            }, ct);
        }

        await unitOfWork.CollectorJobs.AddAsync(new CollectorJob
        {
            GroupId = runId,
            RunId = runId,
            Kind = CollectorJobKind.MetaAds,
            Sequence = 2,
            QueuedAt = now,
        }, ct);

        var google = BuildGoogleJob(runId, runId, sequence: 3, now, connected);
        await unitOfWork.CollectorJobs.AddAsync(google, ct);
        await SaveAsync(ct);

        if (!connected)
        {
            serverFallback.ScheduleGoogleFallback(google.Id);
        }

        return new CollectorRunPlan(google.Id, !connected);
    }

    public async Task<CollectorRunPlan> PlanAdsOnlyAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var groupId = Guid.NewGuid();
        var connected = await IsCollectorConnectedAsync(ct);

        await unitOfWork.CollectorJobs.AddAsync(new CollectorJob
        {
            GroupId = groupId,
            Kind = CollectorJobKind.MetaAds,
            Sequence = 1,
            QueuedAt = now,
        }, ct);

        var google = BuildGoogleJob(groupId, runId: null, sequence: 2, now, connected);
        await unitOfWork.CollectorJobs.AddAsync(google, ct);
        await SaveAsync(ct);

        if (!connected)
        {
            serverFallback.ScheduleGoogleFallback(google.Id);
        }

        return new CollectorRunPlan(google.Id, !connected);
    }

    public async Task<CollectorJobDto?> ClaimNextAsync(Guid deviceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await MaintainAsync(ct);

        // A device works on one job at a time: return the claim it already holds.
        var held = (await unitOfWork.CollectorJobs.FindAsync(
            j => j.ClaimedByDeviceId == deviceId && j.Status == CollectorJobStatus.Claimed, ct)).FirstOrDefault();
        if (held is not null)
        {
            held.Heartbeat(now);
            unitOfWork.CollectorJobs.Update(held);
            await SaveAsync(ct);
            return await ToDtoAsync(held, ct);
        }

        var candidates = await unitOfWork.CollectorJobs.FindAsync(
            j => j.Status == CollectorJobStatus.Queued && j.Source == CollectorJob.DesktopSource, ct);

        foreach (var candidate in candidates.OrderBy(j => j.QueuedAt).ThenBy(j => j.Sequence))
        {
            if (await HasBlockingPredecessorAsync(candidate, ct))
            {
                continue;
            }

            candidate.Claim(deviceId, now);
            unitOfWork.CollectorJobs.Update(candidate);

            try
            {
                await SaveAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another device claimed it first. The app polls again rather than looping here.
                return null;
            }

            return await ToDtoAsync(candidate, ct);
        }

        return null;
    }

    public async Task<bool> HeartbeatAsync(Guid deviceId, Guid jobId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var job = (await unitOfWork.CollectorJobs.FindAsync(
            j => j.Id == jobId && j.ClaimedByDeviceId == deviceId && j.Status == CollectorJobStatus.Claimed, ct)).FirstOrDefault();
        if (job is null)
        {
            return false;
        }

        job.Heartbeat(now);
        unitOfWork.CollectorJobs.Update(job);
        await SaveAsync(ct);
        return true;
    }

    public async Task<CollectorCompletionResult> CompleteAsync(
        Guid deviceId,
        Guid jobId,
        CompleteCollectorJobRequest request,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var job = await unitOfWork.CollectorJobs.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            return CollectorCompletionResult.NotFound;
        }

        // A replay (the app retried after a dropped response) gets the same answer as the first call.
        if (job.ClaimedByDeviceId == deviceId && job.IsTerminal)
        {
            return CollectorCompletionResult.AlreadyCompleted;
        }

        if (job.ClaimedByDeviceId != deviceId || job.Status != CollectorJobStatus.Claimed)
        {
            return CollectorCompletionResult.NotClaimedByDevice;
        }

        var summary = request.Summary ?? BuildSummary(request.Pages);
        var pagesJson = request.Pages is null ? null : JsonSerializer.Serialize(request.Pages);

        if (string.Equals(request.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            job.Fail(now, summary, pagesJson);
        }
        else
        {
            job.Complete(now, summary, pagesJson);
        }

        unitOfWork.CollectorJobs.Update(job);
        await SaveAsync(ct);
        await ReleaseSuccessorsAsync(job, now, ct);

        if (job.Kind is CollectorJobKind.MetaAds or CollectorJobKind.GoogleAds)
        {
            await RefreshAdAlertsAsync(ct);
        }

        return CollectorCompletionResult.Completed;
    }

    public Task<CollectorStoreJobState?> GetStoreJobStateAsync(Guid runId, Guid competitorId, CancellationToken ct) =>
        readContext.Query<CollectorJob>()
            .Where(j => j.RunId == runId && j.Kind == CollectorJobKind.StoreCollection && j.CompetitorId == competitorId)
            .Select(j => new CollectorStoreJobState(
                j.Id,
                j.Status.ToString(),
                j.Summary,
                j.QueuedAt,
                j.LastHeartbeatAt,
                j.Status == CollectorJobStatus.Claimed,
                j.ClaimedByDeviceId))
            .FirstOrDefaultAsync(ct);

    public async Task MaintainAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var modified = false;
        var terminated = new List<CollectorJob>();

        var claimed = await unitOfWork.CollectorJobs.FindAsync(j => j.Status == CollectorJobStatus.Claimed, ct);
        foreach (var job in claimed)
        {
            if (job.ClaimedByDeviceId is not null && job.LastHeartbeatAt < now - LeaseDuration)
            {
                job.ReleaseExpiredClaim(now);
                if (job.Status == CollectorJobStatus.Queued)
                {
                    job.QueuedAt = now;
                }

                unitOfWork.CollectorJobs.Update(job);
                modified = true;
                if (job.IsTerminal)
                {
                    terminated.Add(job);
                }
            }
            else if (job.ClaimedByDeviceId is null && job.Source == CollectorJob.ServerSource &&
                     job.LastHeartbeatAt < now - ServerJobTimeout)
            {
                job.Fail(now, "Server fallback did not finish within the time limit.", null);
                unitOfWork.CollectorJobs.Update(job);
                modified = true;
                terminated.Add(job);
            }
        }

        var stale = await unitOfWork.CollectorJobs.FindAsync(
            j => j.Status == CollectorJobStatus.Queued && j.Source == CollectorJob.DesktopSource && j.QueuedAt < now - ClaimWindow, ct);
        foreach (var job in stale)
        {
            // Only jobs that are actually available count: a blocked job is waiting on earlier work, not on the app.
            if (await HasBlockingPredecessorAsync(job, ct))
            {
                continue;
            }

            job.Skip(now, SkippedNotRunning);
            unitOfWork.CollectorJobs.Update(job);
            modified = true;
            terminated.Add(job);
        }

        if (!modified)
        {
            return;
        }

        await SaveAsync(ct);
        foreach (var job in terminated)
        {
            await ReleaseSuccessorsAsync(job, now, ct);
        }
    }

    public async Task<CollectorStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - ConnectedWindow;

        var connectedCount = await readContext.Query<CollectorDevice>()
            .CountAsync(d => d.RevokedAt == null && d.LastSeenAt != null && d.LastSeenAt >= cutoff, ct);

        var active = await readContext.Query<CollectorJob>()
            .Where(j => j.Status == CollectorJobStatus.Claimed)
            .OrderByDescending(j => j.LastHeartbeatAt)
            .Select(j => new
            {
                j.Id,
                j.Kind,
                j.RunId,
                j.CompetitorSlug,
                j.GroupId,
                j.ClaimedByDeviceId,
                j.ClaimedAt,
                j.LastHeartbeatAt,
            })
            .FirstOrDefaultAsync(ct);

        var queued = await readContext.Query<CollectorJob>()
            .CountAsync(j => j.Status == CollectorJobStatus.Queued && j.Source == CollectorJob.DesktopSource, ct);

        // The progress pill follows the group that is in flight, or else the newest group still waiting.
        var groupId = active?.GroupId ?? await readContext.Query<CollectorJob>()
            .Where(j => j.Status == CollectorJobStatus.Queued)
            .OrderByDescending(j => j.QueuedAt)
            .Select(j => (Guid?)j.GroupId)
            .FirstOrDefaultAsync(ct);

        var completed = 0;
        var total = 0;
        if (groupId is { } group)
        {
            var groupJobs = readContext.Query<CollectorJob>().Where(j => j.GroupId == group);
            total = await groupJobs.CountAsync(ct);
            completed = await groupJobs.CountAsync(j =>
                j.Status == CollectorJobStatus.Completed ||
                j.Status == CollectorJobStatus.Failed ||
                j.Status == CollectorJobStatus.Skipped, ct);
        }

        CollectorActiveJobDto? activeDto = null;
        if (active is not null)
        {
            string? deviceName = null;
            if (active.ClaimedByDeviceId is { } deviceId)
            {
                deviceName = await readContext.Query<CollectorDevice>()
                    .Where(d => d.Id == deviceId)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync(ct);
            }

            activeDto = new CollectorActiveJobDto(
                active.Id,
                active.Kind.ToString(),
                active.RunId,
                active.CompetitorSlug,
                deviceName,
                active.ClaimedAt,
                active.LastHeartbeatAt);
        }

        return new CollectorStatusDto(connectedCount > 0, connectedCount, activeDto, completed, total, queued);
    }

    public async Task<IReadOnlyList<CollectorJobSummaryDto>> GetRunJobsAsync(Guid runId, CancellationToken ct)
    {
        var jobs = await readContext.Query<CollectorJob>()
            .Where(j => j.GroupId == runId)
            .OrderBy(j => j.Sequence)
            .ThenBy(j => j.QueuedAt)
            .ToListAsync(ct);

        return jobs
            .Select(j => new CollectorJobSummaryDto(
                j.Id,
                j.Kind.ToString(),
                j.Source,
                j.Status.ToString(),
                j.CompetitorSlug,
                j.Summary,
                j.QueuedAt,
                j.FinishedAt))
            .ToList();
    }

    public async Task<bool> IsCollectorConnectedAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - ConnectedWindow;
        return await readContext.Query<CollectorDevice>()
            .AnyAsync(d => d.RevokedAt == null && d.LastSeenAt != null && d.LastSeenAt >= cutoff, ct);
    }

    // Saves, then detaches everything. Repository reads are untracked, so an entity saved earlier in the
    // same call must not stay tracked, or a later read-then-Update would attach a second copy of it.
    private async Task SaveAsync(CancellationToken ct)
    {
        await unitOfWork.SaveChangesAsync(ct);
        unitOfWork.ClearTracker();
    }

    private static CollectorJob BuildGoogleJob(Guid groupId, Guid? runId, int sequence, DateTimeOffset now, bool connected)
    {
        var job = new CollectorJob
        {
            GroupId = groupId,
            RunId = runId,
            Kind = CollectorJobKind.GoogleAds,
            Sequence = sequence,
            QueuedAt = now,
            Source = connected ? CollectorJob.DesktopSource : CollectorJob.ServerSource,
        };

        // Server-run jobs never enter the desktop queue; they are marked as held by the server.
        if (!connected)
        {
            job.Status = CollectorJobStatus.Claimed;
            job.LastHeartbeatAt = now;
        }

        return job;
    }

    // A queued job can run only after every earlier job in its group has finished.
    private Task<bool> HasBlockingPredecessorAsync(CollectorJob job, CancellationToken ct) =>
        readContext.Query<CollectorJob>().AnyAsync(j =>
            j.GroupId == job.GroupId &&
            j.Sequence < job.Sequence &&
            j.Status != CollectorJobStatus.Completed &&
            j.Status != CollectorJobStatus.Failed &&
            j.Status != CollectorJobStatus.Skipped, ct);

    // When a job finishes, the queued jobs behind it that are now unblocked start their claim window.
    private async Task ReleaseSuccessorsAsync(CollectorJob finished, DateTimeOffset now, CancellationToken ct)
    {
        var waiting = await unitOfWork.CollectorJobs.FindAsync(
            j => j.GroupId == finished.GroupId && j.Status == CollectorJobStatus.Queued && j.Sequence > finished.Sequence, ct);

        var released = false;
        foreach (var job in waiting)
        {
            if (await HasBlockingPredecessorAsync(job, ct))
            {
                continue;
            }

            job.QueuedAt = now;
            unitOfWork.CollectorJobs.Update(job);
            released = true;
        }

        if (released)
        {
            await SaveAsync(ct);
        }
    }

    private async Task RefreshAdAlertsAsync(CancellationToken ct)
    {
        try
        {
            await alertService.SyncAlertsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ad alerts could not be recomputed after an ads job completed.");
        }
    }

    private async Task<CollectorJobDto> ToDtoAsync(CollectorJob job, CancellationToken ct)
    {
        CollectorJobCompetitorDto? competitor = null;
        if (job.CompetitorId is { } competitorId)
        {
            competitor = await readContext.Query<Competitor>()
                .Where(c => c.Id == competitorId)
                .Select(c => new CollectorJobCompetitorDto(
                    c.Id,
                    c.Key,
                    c.Name,
                    c.BaseUrl,
                    c.Platform.ToString(),
                    c.MinDelayMs,
                    c.MaxConcurrency))
                .FirstOrDefaultAsync(ct);
        }

        return new CollectorJobDto(job.Id, job.Kind.ToString(), job.RunId, job.Attempts, competitor);
    }

    private static string BuildSummary(IReadOnlyList<CollectorPageReport>? pages)
    {
        if (pages is null || pages.Count == 0)
        {
            return "Completed.";
        }

        var ads = pages.Sum(p => p.TotalCollected);
        var notCollected = pages.Count(p => !string.Equals(p.Status, "Success", StringComparison.OrdinalIgnoreCase));
        return notCollected == 0
            ? $"{pages.Count} page(s), {ads} ad(s)."
            : $"{pages.Count} page(s), {ads} ad(s), {notCollected} page(s) not collected.";
    }
}
