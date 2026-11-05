using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Runs.Queries.GetRunById;

public class GetRunByIdQueryHandler(IReadDbContext readContext, ICacheService cache)
    : IRequestHandler<GetRunByIdQuery, RunDto>
{
    public async Task<RunDto> Handle(GetRunByIdQuery request, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            $"runs:{request.Id}",
            async token =>
            {
                var run = await readContext.Query<CollectionRun>()
                    .FirstOrDefaultAsync(r => r.Id == request.Id, token)
                    ?? throw new NotFoundException(nameof(CollectionRun), request.Id);

                var competitorRuns = await (
                    from cr in readContext.Query<CompetitorRun>()
                    join c in readContext.Query<Competitor>() on cr.CompetitorId equals c.Id
                    where cr.RunId == request.Id
                    select new CompetitorRunDto(
                        cr.CompetitorId,
                        c.Key,
                        c.Name,
                        c.IsReference,
                        cr.Status.ToString(),
                        cr.Coverage.ToString(),
                        cr.Pages,
                        cr.Products,
                        cr.Errors))
                    .ToListAsync(token);

                return new RunDto(
                    run.Id,
                    run.Trigger.ToString(),
                    run.Status.ToString(),
                    run.StartedAt,
                    run.FinishedAt,
                    competitorRuns,
                    run.MatchesCount,
                    run.PhasesJson);
            },
            TimeSpan.FromMinutes(1),
            tags: ["runs"],
            ct);
    }
}
