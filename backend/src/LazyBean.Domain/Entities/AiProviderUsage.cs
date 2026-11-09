using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

/// <summary>Requests and tokens used per provider per UTC day.</summary>
public class AiProviderUsage : BaseEntity, IAggregateRoot
{
    public string Provider { get; set; } = string.Empty;
    public DateOnly UsageDate { get; set; }
    public int RequestCount { get; set; }
    public long TokenCount { get; set; }
}
