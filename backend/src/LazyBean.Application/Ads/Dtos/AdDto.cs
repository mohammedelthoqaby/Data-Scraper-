namespace LazyBean.Application.Ads.Dtos;

public record AdDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    Guid IdentityId,
    string IdentityName,
    Guid? CampaignId,
    string ExternalAdId,
    string Platform,
    string? Title,
    string? Body,
    string? LinkUrl,
    string? SnapshotUrl,
    DateTimeOffset? DeliveryStartDate,
    DateTimeOffset? DeliveryStopDate,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string Status,
    string CreativeHash);
