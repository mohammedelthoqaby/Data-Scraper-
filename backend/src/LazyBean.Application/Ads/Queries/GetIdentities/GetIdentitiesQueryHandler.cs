using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetIdentities;

public class GetIdentitiesQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetIdentitiesQuery, IReadOnlyList<AdvertiserIdentityDto>>
{
    public async Task<IReadOnlyList<AdvertiserIdentityDto>> Handle(GetIdentitiesQuery request, CancellationToken ct)
    {
        var query = from i in readContext.Query<AdvertiserIdentity>()
                    join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
                    select new
                    {
                        Identity = i,
                        Competitor = c
                    };

        if (request.CompetitorId.HasValue)
        {
            query = query.Where(x => x.Identity.CompetitorId == request.CompetitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            query = query.Where(x => x.Identity.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<AdvertiserIdentityStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Identity.Status == status);
        }

        return await query
            .OrderByDescending(x => x.Identity.CreatedAt)
            .Select(x => new AdvertiserIdentityDto(
                x.Identity.Id,
                x.Competitor.Id,
                x.Competitor.Key,
                x.Competitor.Name,
                x.Identity.Platform.ToString(),
                x.Identity.ExternalId,
                x.Identity.Name,
                x.Identity.Status.ToString(),
                x.Identity.LibraryUrl,
                x.Identity.OwnershipEvidenceUrl,
                x.Identity.Notes,
                x.Identity.ReviewedAt,
                x.Identity.CreatedAt))
            .ToListAsync(ct);
    }
}
