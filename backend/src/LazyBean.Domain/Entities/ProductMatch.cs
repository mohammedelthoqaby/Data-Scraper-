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

    /// <summary>Short explanation, for example the AI reason. Null for rule matches.</summary>
    public string? Reason { get; set; }

    /// <summary>True when the match is a bundle or multipack whose unit price was normalized to compare.</summary>
    public bool IsBundle { get; set; }
}
