using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class AlertHistory : BaseEntity, IAggregateRoot
{
    public Guid AlertId { get; set; }
    public string Action { get; set; } = string.Empty; // Created, Done, Snoozed, Dismissed, Reopened
    public string? Note { get; set; }
    public string PerformedBy { get; set; } = "system";
}
