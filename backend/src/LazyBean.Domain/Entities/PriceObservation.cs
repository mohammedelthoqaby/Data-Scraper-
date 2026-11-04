using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class PriceObservation : BaseEntity, IAggregateRoot
{
    public Guid VariantId { get; set; }
    public Guid RunId { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool? InStock { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}
