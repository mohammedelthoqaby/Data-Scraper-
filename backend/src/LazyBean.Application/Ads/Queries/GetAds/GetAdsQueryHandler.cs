using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetAds;

public class GetAdsQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetAdsQuery, GetAdsResponseDto>
{
    public async Task<GetAdsResponseDto> Handle(GetAdsQuery request, CancellationToken ct)
    {
        var query = from a in readContext.Query<Ad>()
                    join c in readContext.Query<Competitor>() on a.CompetitorId equals c.Id
                    join i in readContext.Query<AdvertiserIdentity>() on a.IdentityId equals i.Id
                    select new
                    {
                        Ad = a,
                        Competitor = c,
                        Identity = i
                    };

        if (request.CompetitorId.HasValue)
        {
            query = query.Where(x => x.Ad.CompetitorId == request.CompetitorId.Value);
        }

        if (request.IdentityId.HasValue)
        {
            query = query.Where(x => x.Ad.IdentityId == request.IdentityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            query = query.Where(x => x.Ad.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<AdStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Ad.Status == status);
        }

        if (request.From.HasValue)
        {
            query = query.Where(x => x.Ad.LastSeenAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(x => x.Ad.FirstSeenAt <= request.To.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.Ad.LastSeenAt)
            .Take(500)
            .Select(x => new AdDto(
                x.Ad.Id,
                x.Competitor.Id,
                x.Competitor.Key,
                x.Competitor.Name,
                x.Identity.Id,
                x.Identity.Name,
                x.Ad.CampaignId,
                x.Ad.ExternalAdId,
                x.Ad.Platform.ToString(),
                x.Ad.Title,
                x.Ad.Body,
                x.Ad.LinkUrl,
                x.Ad.SnapshotUrl,
                x.Ad.DeliveryStartDate,
                x.Ad.DeliveryStopDate,
                x.Ad.FirstSeenAt,
                x.Ad.LastSeenAt,
                x.Ad.Status.ToString(),
                x.Ad.CreativeHash))
            .ToListAsync(ct);

        return new GetAdsResponseDto(totalCount, items);
    }
}
