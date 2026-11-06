using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetCampaigns;

// One page of campaigns with their variants. Paging and totals are done in the database, so no campaign is dropped
// by a cap. A campaign's variants are every ad of its campaign, shown under the same filters as the Ads page.
public class GetCampaignsQueryHandler(IReadDbContext readContext, AdsOptions options, ISender sender)
    : IRequestHandler<GetCampaignsQuery, CampaignPageDto>
{
    public async Task<CampaignPageDto> Handle(GetCampaignsQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromDays(options.GoogleActiveWindowDays);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var campaigns = from c in readContext.Query<AdCampaign>().AsNoTracking()
                        join comp in readContext.Query<Competitor>().AsNoTracking() on c.CompetitorId equals comp.Id
                        join identity in readContext.Query<AdvertiserIdentity>().AsNoTracking() on c.IdentityId equals identity.Id
                        select new { Campaign = c, Competitor = comp, IdentityName = identity.Name };

        if (request.CompetitorId.HasValue)
        {
            campaigns = campaigns.Where(x => x.Campaign.CompetitorId == request.CompetitorId.Value);
        }
        else
        {
            // Our own (reference) campaigns are not competitor campaigns.
            campaigns = campaigns.Where(x => !x.Competitor.IsReference);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            campaigns = campaigns.Where(x => x.Campaign.Platform == platform);
        }

        // A campaign with no creatives has nothing to show.
        campaigns = campaigns.Where(x => readContext.Query<Ad>().Any(a => a.CampaignId == x.Campaign.Id));

        if (string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            campaigns = campaigns.Where(x => readContext.Query<Ad>().Any(a =>
                a.CampaignId == x.Campaign.Id && a.Status == AdStatus.Active && (a.Platform != AdPlatform.Google || a.LastSeenAt >= cutoff)));
        }
        else if (string.Equals(request.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            campaigns = campaigns.Where(x => !readContext.Query<Ad>().Any(a =>
                a.CampaignId == x.Campaign.Id && a.Status == AdStatus.Active && (a.Platform != AdPlatform.Google || a.LastSeenAt >= cutoff)));
        }

        var totalCount = await campaigns.CountAsync(ct);
        var rows = await campaigns
            .OrderByDescending(x => x.Campaign.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new CampaignPageDto(totalCount, page, pageSize, []);
        }

        var ids = rows.Select(r => r.Campaign.Id).ToList();
        var counts = await readContext.Query<Ad>()
            .AsNoTracking()
            .Where(a => a.CampaignId != null && ids.Contains(a.CampaignId.Value))
            .GroupBy(a => a.CampaignId)
            .Select(g => new
            {
                CampaignId = g.Key,
                Total = g.Count(),
                Active = g.Count(a => a.Status == AdStatus.Active && (a.Platform != AdPlatform.Google || a.LastSeenAt >= cutoff)),
            })
            .ToListAsync(ct);
        var countsByCampaign = counts.ToDictionary(c => c.CampaignId);

        var variants = await sender.Send(new GetAdsQuery(
            CompetitorId: request.CompetitorId,
            Platform: request.Platform,
            Status: request.Status,
            CampaignIds: ids), ct);
        var variantsByCampaign = variants.Ads
            .Where(a => a.CampaignId.HasValue)
            .GroupBy(a => a.CampaignId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AdDto>)g.ToList());

        var items = rows.Select(row =>
        {
            var campaign = row.Campaign;
            countsByCampaign.TryGetValue(campaign.Id, out var count);
            var total = count?.Total ?? 0;
            var active = count?.Active ?? 0;
            var list = variantsByCampaign.TryGetValue(campaign.Id, out var found) ? found : Array.Empty<AdDto>();

            return new AdCampaignDto(
                campaign.Id,
                row.Competitor.Id,
                row.Competitor.Key,
                row.Competitor.Name,
                campaign.IdentityId,
                campaign.CreativeHash,
                campaign.Platform.ToString(),
                campaign.Body,
                campaign.FirstSeenAt,
                campaign.LastSeenAt,
                active > 0 ? "Active" : "Inactive",
                total,
                active,
                total - active,
                list,
                row.IdentityName);
        }).ToList();

        return new CampaignPageDto(totalCount, page, pageSize, items);
    }
}
