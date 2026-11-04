using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Offer : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public Guid RunId { get; set; }
    public OfferType Type { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public Guid? ProductId { get; set; }
}
