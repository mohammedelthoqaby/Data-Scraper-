using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

// One unit of work for a paired collector app. Jobs are grouped (a run, or an ad-only batch) and
// run in Sequence order within the group. A claimed job whose app stops heartbeating is released
// back to the queue until MaxAttempts is reached.
public class CollectorJob : BaseEntity, IAggregateRoot
{
    public const int MaxAttempts = 3;
    public const string DesktopSource = "desktop";
    public const string ServerSource = "server";

    public Guid GroupId { get; set; }
    public Guid? RunId { get; set; }

    // Who runs the job: a paired desktop app, or the server (Google fallback when no app is connected).
    public string Source { get; set; } = DesktopSource;
    public CollectorJobKind Kind { get; set; }
    public int Sequence { get; set; }
    public Guid? CompetitorId { get; set; }
    public string? CompetitorSlug { get; set; }
    public CollectorJobStatus Status { get; set; } = CollectorJobStatus.Queued;
    public Guid? ClaimedByDeviceId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public int Attempts { get; set; }

    // Postgres row version (xmin). Carried on the entity so an untracked read-then-Update checks it.
    public uint RowVersion { get; set; }
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }

    // Human-readable outcome: "Skipped (collector not running)", "Completed by server fallback", an error.
    public string? Summary { get; set; }

    // Per-page counts and errors reported by the app when it completes the job.
    public string? ResultJson { get; set; }

    public bool IsTerminal => Status is CollectorJobStatus.Completed or CollectorJobStatus.Failed or CollectorJobStatus.Skipped;

    public void Claim(Guid deviceId, DateTimeOffset now)
    {
        Status = CollectorJobStatus.Claimed;
        ClaimedByDeviceId = deviceId;
        ClaimedAt = now;
        LastHeartbeatAt = now;
        Attempts++;
        UpdatedAt = now;
    }

    public void Heartbeat(DateTimeOffset now)
    {
        LastHeartbeatAt = now;
        UpdatedAt = now;
    }

    // Lease expired (the app went away mid-job). Returns the job to the queue, or fails it once
    // it has used all its attempts.
    public void ReleaseExpiredClaim(DateTimeOffset now)
    {
        ClaimedByDeviceId = null;
        ClaimedAt = null;
        LastHeartbeatAt = null;
        UpdatedAt = now;

        if (Attempts >= MaxAttempts)
        {
            Status = CollectorJobStatus.Failed;
            FinishedAt = now;
            Summary = "Collector stopped responding and the retry limit was reached.";
        }
        else
        {
            Status = CollectorJobStatus.Queued;
        }
    }

    // The app is shutting down on purpose (Exit, or sign-out) while it holds this job. The job goes back to
    // the queue with a fresh claim window. A voluntary release is not a failed attempt, so the claim it
    // gives back does not count toward MaxAttempts.
    public void ReleaseByDevice(DateTimeOffset now)
    {
        Status = CollectorJobStatus.Queued;
        ClaimedByDeviceId = null;
        ClaimedAt = null;
        LastHeartbeatAt = null;
        Attempts = Math.Max(0, Attempts - 1);
        QueuedAt = now;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now, string? summary, string? resultJson)
    {
        Status = CollectorJobStatus.Completed;
        FinishedAt = now;
        Summary = summary;
        ResultJson = resultJson;
        UpdatedAt = now;
    }

    public void Fail(DateTimeOffset now, string summary, string? resultJson)
    {
        Status = CollectorJobStatus.Failed;
        FinishedAt = now;
        Summary = summary;
        ResultJson = resultJson;
        UpdatedAt = now;
    }

    public void Skip(DateTimeOffset now, string summary)
    {
        Status = CollectorJobStatus.Skipped;
        FinishedAt = now;
        Summary = summary;
        UpdatedAt = now;
    }
}
