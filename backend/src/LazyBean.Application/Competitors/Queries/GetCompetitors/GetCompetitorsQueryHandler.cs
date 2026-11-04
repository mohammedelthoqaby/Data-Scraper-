using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Competitors.Dtos;
using LazyBean.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Competitors.Queries.GetCompetitors;

public class GetCompetitorsQueryHandler(IReadDbContext readContext, ICacheService cache)
    : IRequestHandler<GetCompetitorsQuery, IReadOnlyList<CompetitorDto>>
{
    public async Task<IReadOnlyList<CompetitorDto>> Handle(GetCompetitorsQuery request, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            "competitors:all",
            async token => (IReadOnlyList<CompetitorDto>)await readContext.Query<Competitor>()
                .Select(c => new CompetitorDto(
                    c.Id, c.Key, c.Name, c.BaseUrl, c.Platform.ToString(), c.IsReference, c.Enabled, c.MinDelayMs, c.MaxConcurrency))
                .ToListAsync(token),
            TimeSpan.FromMinutes(5),
            tags: ["competitors"],
            ct);
    }
}
