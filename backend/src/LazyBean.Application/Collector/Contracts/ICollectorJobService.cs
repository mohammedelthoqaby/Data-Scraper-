using LazyBean.Application.Collector.Dtos;

namespace LazyBean.Application.Collector.Contracts;

// Work queue for the desktop collector. Jobs in a group run in Sequence order: the store collection
// of each desktop competitor, then Meta ads, then Google ads. A job is only handed out once every
// earlier job in its group has finished, and a job nobody claims within the claim window is Skipped.
public interface ICollectorJobService
{
    // Called when a run starts. Creates the run's jobs once (safe to call again on a resumed run).
    Task<CollectorRunPlan> PlanRunAsync(Guid runId, IReadOnlyList<CollectorStoreTarget> stores, CancellationToken ct);

    // Dashboard "Collect ads now": Meta and Google jobs with no run.
    Task<CollectorRunPlan> PlanAdsOnlyAsync(CancellationToken ct);

    // Returns the next job for this device (its current claim, or a newly claimed job), or null.
    Task<CollectorJobDto?> ClaimNextAsync(Guid deviceId, CancellationToken ct);

    Task<bool> HeartbeatAsync(Guid deviceId, Guid jobId, CancellationToken ct);

    // The app is exiting while it holds a claim: the job goes back to the queue at once. False when the
    // device does not hold the claim (it already finished, or the lease expired and someone else took it).
    Task<bool> ReleaseAsync(Guid deviceId, Guid jobId, CancellationToken ct);

    Task<CollectorCompletionResult> CompleteAsync(Guid deviceId, Guid jobId, CompleteCollectorJobRequest request, CancellationToken ct);

    Task<CollectorStoreJobState?> GetStoreJobStateAsync(Guid runId, Guid competitorId, CancellationToken ct);

    // Returns jobs to the queue when their app went quiet, and skips jobs nobody picked up in time.
    Task MaintainAsync(CancellationToken ct);

    Task<bool> IsCollectorConnectedAsync(CancellationToken ct);

    // Header pill and Settings > Collector: connection, the job in progress and the active group's progress.
    Task<CollectorStatusDto> GetStatusAsync(CancellationToken ct);

    // A run's per-source status: which jobs ran, on the desktop app or the server, and how they ended.
    Task<IReadOnlyList<CollectorJobSummaryDto>> GetRunJobsAsync(Guid runId, CancellationToken ct);
}
