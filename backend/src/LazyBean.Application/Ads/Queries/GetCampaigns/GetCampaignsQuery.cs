using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetCampaigns;

public record GetCampaignsQuery(
    Guid? CompetitorId = null,
    string? Platform = null,
    string? Status = null) : IRequest<IReadOnlyList<AdCampaignDto>>;
