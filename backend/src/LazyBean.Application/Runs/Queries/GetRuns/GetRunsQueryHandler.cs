using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Runs.Queries.GetRuns;

public class GetRunsQueryHandler(IReadDbContext readContext, ICacheService cache)
    : IRequestHandler<GetRunsQuery, IReadOnlyList<RunDto>>
{
    public async Task<IReadOnlyList<RunDto>> Handle(GetRunsQuery request, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            "runs:all",
            async token => (IReadOnlyList<RunDto>)await readContext.Query<CollectionRun>()
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new RunDto(
                    r.Id, r.Trigger.ToString(), r.Status.ToString(), r.StartedAt, r.FinishedAt,
                    Array.Empty<CompetitorRunDto>()))
                .ToListAsync(token),
            TimeSpan.FromMinutes(1),
            tags: ["runs"],
            ct);
    }
}
