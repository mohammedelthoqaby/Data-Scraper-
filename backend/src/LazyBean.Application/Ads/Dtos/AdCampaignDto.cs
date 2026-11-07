namespace LazyBean.Application.Ads.Dtos;

// One campaign: a Google advertiser (all its creatives) or a Meta creative body. Ads are its variants.
public record AdCampaignDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    Guid IdentityId,
    string CreativeHash,
    string Platform,
    string? Body,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string Status,
    int AdCount,
    int ActiveAdCount,
    int InactiveAdCount,
    IReadOnlyList<AdDto> Ads,
    string IdentityName = "");

public record CampaignPageDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AdCampaignDto> Items);
