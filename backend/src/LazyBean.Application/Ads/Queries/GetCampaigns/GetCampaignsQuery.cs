using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetCampaigns;

// One page of campaigns (20 by default, at most 100). Status "Active" lists campaigns with an Active creative; "Inactive"
// lists the rest.
public record GetCampaignsQuery(
    Guid? CompetitorId = null,
    string? Platform = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20) : IRequest<CampaignPageDto>;
