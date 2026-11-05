using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class CollectionRun : BaseEntity, IAggregateRoot
{
    public RunTrigger Trigger { get; set; }
    public CollectionRunStatus Status { get; set; } = CollectionRunStatus.Queued;
    public string? TargetCompetitorIdsJson { get; set; }
    public string? PhasesJson { get; set; }
    public int? MatchesCount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
