using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetCampaigns;

public class GetCampaignsQueryHandler(IReadDbContext readContext, ISender sender)
    : IRequestHandler<GetCampaignsQuery, IReadOnlyList<AdCampaignDto>>
{
    public async Task<IReadOnlyList<AdCampaignDto>> Handle(GetCampaignsQuery request, CancellationToken ct)
    {
        // 1. Fetch campaigns
        var campaignQuery = from c in readContext.Query<AdCampaign>()
                            join comp in readContext.Query<Competitor>() on c.CompetitorId equals comp.Id
                            select new
                            {
                                Campaign = c,
                                Competitor = comp
                            };

        if (request.CompetitorId.HasValue)
        {
            campaignQuery = campaignQuery.Where(x => x.Campaign.CompetitorId == request.CompetitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            campaignQuery = campaignQuery.Where(x => x.Campaign.Platform == platform);
        }

        var campaigns = await campaignQuery.ToListAsync(ct);
        if (campaigns.Count == 0)
        {
            return [];
        }

        // 2. Fetch all ads with full enrichment via GetAdsQuery
        var adsResponse = await sender.Send(new GetAdsQuery(
            CompetitorId: request.CompetitorId,
            Platform: request.Platform,
            Status: request.Status), ct);

        var adsByCampaign = adsResponse.Ads
            .Where(a => a.CampaignId.HasValue)
            .GroupBy(a => a.CampaignId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<AdCampaignDto>();

        foreach (var item in campaigns)
        {
            var camp = item.Campaign;
            var comp = item.Competitor;

            adsByCampaign.TryGetValue(camp.Id, out var adsList);
            adsList ??= [];

            // If a status filter was supplied and there are no ads matching that status, skip if requested
            if (!string.IsNullOrWhiteSpace(request.Status) && adsList.Count == 0)
            {
                continue;
            }

            var activeCount = adsList.Count(a => string.Equals(a.Status, "Active", StringComparison.OrdinalIgnoreCase));
            var inactiveCount = adsList.Count - activeCount;

            var body = !string.IsNullOrWhiteSpace(camp.Body)
                ? camp.Body
                : adsList.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Body))?.Body;

            var status = activeCount > 0 ? "Active" : (adsList.Count > 0 ? "Inactive" : camp.Status.ToString());

            result.Add(new AdCampaignDto(
                camp.Id,
                comp.Id,
                comp.Key,
                comp.Name,
                camp.IdentityId,
                camp.CreativeHash,
                camp.Platform.ToString(),
                body,
                camp.FirstSeenAt,
                camp.LastSeenAt,
                status,
                adsList.Count,
                activeCount,
                inactiveCount,
                adsList));
        }

        return result
            .OrderByDescending(c => c.AdCount)
            .ThenByDescending(c => c.LastSeenAt)
            .ToList();
    }
}
