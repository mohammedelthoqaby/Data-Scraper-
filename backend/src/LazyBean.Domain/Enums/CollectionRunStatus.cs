namespace LazyBean.Domain.Enums;

public enum CollectionRunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Partial = 4,
    Cancelled = 5,
    Interrupted = 6
}
