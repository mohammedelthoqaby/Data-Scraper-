namespace LazyBean.Domain.Enums;

public enum CollectorJobStatus
{
    Queued = 0,
    Claimed = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
}
