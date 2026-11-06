using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetAds;

// Page and PageSize page the list (20 by default, at most 100). CampaignIds returns every ad of those campaigns,
// unpaged, for the campaign cards.
public record GetAdsQuery(
    Guid? CompetitorId = null,
    Guid? IdentityId = null,
    string? Platform = null,
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyList<Guid>? CampaignIds = null) : IRequest<GetAdsResponseDto>;

public record GetAdsResponseDto(
    int TotalCount,
    IReadOnlyList<AdDto> Ads,
    int Page = 1,
    int PageSize = 20);
