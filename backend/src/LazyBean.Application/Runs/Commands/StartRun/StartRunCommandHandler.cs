using System.Text.Json;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Runs.Commands.StartRun;

// Only creates the Queued row and enqueues it - the actual fetch/normalize/persist pipeline
// runs later, out of band, in the BackgroundService that drains IRunQueue.
public class StartRunCommandHandler(IUnitOfWork unitOfWork, IRunQueue runQueue, ICacheService cache)
    : IRequestHandler<StartRunCommand, StartRunResultDto>
{
    public async Task<StartRunResultDto> Handle(StartRunCommand request, CancellationToken ct)
    {
        var activeRun = await unitOfWork.CollectionRuns.GetActiveRunAsync(ct);
        if (activeRun is not null)
        {
            return new StartRunResultDto(activeRun.Id, activeRun.Status.ToString().ToLowerInvariant(), true);
        }

        string? targetCompetitorIdsJson = null;
        if (request.CompetitorIds is { Count: > 0 })
        {
            targetCompetitorIdsJson = JsonSerializer.Serialize(request.CompetitorIds);
        }

        var run = new CollectionRun
        {
            Trigger = request.Trigger,
            Status = CollectionRunStatus.Queued,
            TargetCompetitorIdsJson = targetCompetitorIdsJson
        };
        await unitOfWork.CollectionRuns.AddAsync(run, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync("runs", ct);
        await runQueue.EnqueueAsync(run.Id, ct);

        return new StartRunResultDto(run.Id, run.Status.ToString().ToLowerInvariant(), false);
    }
}
