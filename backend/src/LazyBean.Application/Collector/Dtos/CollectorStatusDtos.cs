namespace LazyBean.Application.Collector.Dtos;

// What the dashboard header and Settings > Collector show about the desktop app.
public sealed record CollectorStatusDto(
    bool Connected,
    int ConnectedDeviceCount,
    CollectorActiveJobDto? ActiveJob,
    int ActiveGroupCompleted,
    int ActiveGroupTotal,
    int QueuedJobCount);

public sealed record CollectorActiveJobDto(
    Guid JobId,
    string Kind,
    Guid? RunId,
    string? CompetitorSlug,
    string? DeviceName,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LastHeartbeatAt);

// One row of a run's per-source status: who ran it (desktop app or server) and how it ended.
public sealed record CollectorJobSummaryDto(
    Guid JobId,
    string Kind,
    string Source,
    string Status,
    string? CompetitorSlug,
    string? Summary,
    DateTimeOffset QueuedAt,
    DateTimeOffset? FinishedAt);
