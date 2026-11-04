namespace LazyBean.Application.Ads.Dtos;

public record AdCampaignDto(
    Guid Id,
    Guid CompetitorId,
    Guid IdentityId,
    string CreativeHash,
    string Platform,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string Status,
    int AdCount,
    IReadOnlyList<AdDto> Ads);
