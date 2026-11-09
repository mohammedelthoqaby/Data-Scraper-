namespace LazyBean.Application.Collector.Dtos;

// A competitor whose catalog is collected by the desktop app in a run.
public sealed record CollectorStoreTarget(Guid CompetitorId, string Slug);

public sealed record CollectorRunPlan(Guid? GoogleJobId, bool GoogleOnServer);

// One unit of work handed to the desktop app. Competitor is set for StoreCollection jobs only.
public sealed record CollectorJobDto(
    Guid Id,
    string Kind,
    Guid? RunId,
    int Attempt,
    CollectorJobCompetitorDto? Competitor,
    // The job's place in its batch (the jobs of one run or one "Collect ads now" press): finished jobs + 1,
    // out of the batch's total. Zero when unknown.
    int BatchPosition = 0,
    int BatchTotal = 0,
    // For a ResolveMetaPage job: the Facebook page name the app should look up.
    string? PageSlug = null);

public sealed record CollectorJobCompetitorDto(
    Guid Id,
    string Slug,
    string Name,
    string BaseUrl,
    string Platform,
    int MinDelayMs,
    int MaxConcurrency);

// Per-page outcome the app reports for an ads job (page ID or advertiser ID, plus counts and errors).
public sealed record CollectorPageReport(string PageId, string Status, int TotalCollected, string? Error);

// Optional body of a heartbeat: what the app is doing right now.
public sealed record CollectorHeartbeatRequest(string? ProgressLabel, int? ProgressDone, int? ProgressTotal);

public sealed record CompleteCollectorJobRequest(string Status, string? Summary, IReadOnlyList<CollectorPageReport>? Pages);

public enum CollectorCompletionResult
{
    Completed,
    AlreadyCompleted,
    NotClaimedByDevice,
    NotFound,
}

// What the run executor needs to decide whether a desktop store job is still worth waiting for.
public sealed record CollectorStoreJobState(
    Guid JobId,
    string Status,
    string? Summary,
    DateTimeOffset QueuedAt,
    DateTimeOffset? LastHeartbeatAt,
    bool Claimed,
    Guid? ClaimedByDeviceId);
