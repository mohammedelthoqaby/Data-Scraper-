using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetIdentities;

public record GetIdentitiesQuery(
    Guid? CompetitorId = null,
    string? Platform = null,
    string? Status = null) : IRequest<IReadOnlyList<AdvertiserIdentityDto>>;
