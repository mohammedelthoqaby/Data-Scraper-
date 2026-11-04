using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetAds;

public record GetAdsQuery(
    Guid? CompetitorId = null,
    Guid? IdentityId = null,
    string? Platform = null,
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<GetAdsResponseDto>;

public record GetAdsResponseDto(
    int TotalCount,
    IReadOnlyList<AdDto> Ads);
