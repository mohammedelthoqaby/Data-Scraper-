using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class CompetitorRun : BaseEntity, IAggregateRoot
{
    public Guid RunId { get; set; }
    public Guid CompetitorId { get; set; }
    public CompetitorRunStatus Status { get; set; } = CompetitorRunStatus.Queued;
    public int Pages { get; set; }
    public int Products { get; set; }
    public int Errors { get; set; }
    public RunCoverage Coverage { get; set; } = RunCoverage.Partial;
}
