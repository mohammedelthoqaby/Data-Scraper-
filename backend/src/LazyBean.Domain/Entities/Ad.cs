using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Ad : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public Guid IdentityId { get; set; }
    public Guid? CampaignId { get; set; }
    public string ExternalAdId { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public string? SnapshotUrl { get; set; }
    public DateTimeOffset? DeliveryStartDate { get; set; }
    public DateTimeOffset? DeliveryStopDate { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public AdStatus Status { get; set; } = AdStatus.Active;
    public string CreativeHash { get; set; } = string.Empty;
    public string? RawDataJson { get; set; }
}
