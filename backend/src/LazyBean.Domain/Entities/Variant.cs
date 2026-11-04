using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class Variant : BaseEntity, IAggregateRoot
{
    public Guid ProductId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int? WeightGrams { get; set; }
    public int? UnitCount { get; set; } // e.g. "10 Capsules" -> 10
    public string? Sku { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public Guid? LastSeenRunId { get; set; }
}
