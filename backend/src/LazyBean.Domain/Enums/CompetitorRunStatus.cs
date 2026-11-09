namespace LazyBean.Domain.Enums;

public enum CompetitorRunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,

    // Product count dropped sharply vs. recent runs - likely a broken adapter, not a
    // real catalog shrink. Products are never marked Removed off a Degraded run.
    Degraded = 4,
    Cancelled = 5,
    Suspicious = 6,
    Interrupted = 7,

    // No LazyBean Collector claimed this competitor's desktop job in time. Products are not touched.
    Skipped = 8
}
