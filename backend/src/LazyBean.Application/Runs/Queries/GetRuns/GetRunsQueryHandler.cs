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
            async token =>
            {
                var runs = await readContext.Query<CollectionRun>()
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(50)
                    .ToListAsync(token);

                if (runs.Count == 0)
                {
                    return (IReadOnlyList<RunDto>)Array.Empty<RunDto>();
                }

                var runIds = runs.Select(r => r.Id).ToList();

                var compRuns = await (
                    from cr in readContext.Query<CompetitorRun>()
                    join c in readContext.Query<Competitor>() on cr.CompetitorId equals c.Id
                    where runIds.Contains(cr.RunId)
                    select new
                    {
                        cr.RunId,
                        Dto = new CompetitorRunDto(
                            cr.CompetitorId,
                            c.Key,
                            c.Name,
                            c.IsReference,
                            cr.Status.ToString(),
                            cr.Coverage.ToString(),
                            cr.Pages,
                            cr.Products,
                            cr.Errors)
                    }
                ).ToListAsync(token);

                var compRunsByRunId = compRuns
                    .GroupBy(x => x.RunId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

                return (IReadOnlyList<RunDto>)runs.Select(r => new RunDto(
                    r.Id,
                    r.Trigger.ToString(),
                    r.Status.ToString(),
                    r.StartedAt,
                    r.FinishedAt,
                    compRunsByRunId.GetValueOrDefault(r.Id, []),
                    r.MatchesCount,
                    r.PhasesJson
                )).ToList();
            },
            TimeSpan.FromMinutes(1),
            tags: ["runs"],
            ct);
    }
}
