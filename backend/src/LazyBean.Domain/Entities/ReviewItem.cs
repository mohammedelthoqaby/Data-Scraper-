using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class ReviewItem : BaseEntity, IAggregateRoot
{
    public ReviewItemKind Kind { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Reason { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
}
