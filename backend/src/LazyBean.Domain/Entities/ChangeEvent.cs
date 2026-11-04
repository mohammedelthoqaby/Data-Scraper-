using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class ChangeEvent : BaseEntity, IAggregateRoot
{
    public Guid RunId { get; set; }
    public Guid? VariantId { get; set; }
    public Guid? AdId { get; set; }
    public ChangeEventType Type { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
