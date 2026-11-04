using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class AdCampaign : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public Guid IdentityId { get; set; }
    public string CreativeHash { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public AdStatus Status { get; set; } = AdStatus.Active;
}
