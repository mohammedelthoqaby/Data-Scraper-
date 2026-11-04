using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class ProductMatch : BaseEntity, IAggregateRoot
{
    public Guid CompetitorVariantId { get; set; }
    public Guid ReferenceVariantId { get; set; }
    public decimal Score { get; set; }
    public MatchMethod Method { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Auto;
}
