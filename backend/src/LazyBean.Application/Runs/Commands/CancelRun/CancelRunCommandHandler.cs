using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Runs.Commands.CancelRun;

public class CancelRunCommandHandler(
    IUnitOfWork unitOfWork,
    IRunCancellationTokenRegistry cancellationRegistry,
    ICacheService cache)
    : IRequestHandler<CancelRunCommand, CancelRunResultDto>
{
    public async Task<CancelRunResultDto> Handle(CancelRunCommand request, CancellationToken ct)
    {
        var run = await unitOfWork.CollectionRuns.GetByIdAsync(request.RunId, ct)
            ?? throw new NotFoundException(nameof(CollectionRun), request.RunId);

        if (run.Status != CollectionRunStatus.Queued && run.Status != CollectionRunStatus.Running)
        {
            return new CancelRunResultDto(run.Id, run.Status.ToString().ToLowerInvariant());
        }

        cancellationRegistry.TryCancel(request.RunId);

        run.Status = CollectionRunStatus.Cancelled;
        run.FinishedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync("runs", ct);

        return new CancelRunResultDto(run.Id, "cancelled");
    }
}
