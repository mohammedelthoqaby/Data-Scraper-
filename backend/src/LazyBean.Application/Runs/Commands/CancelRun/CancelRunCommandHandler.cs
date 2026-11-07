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

        run.Status = CollectionRunStatus.Cancelled;
        run.FinishedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        // Signal the executor only after Cancelled is saved. Otherwise it can read the run while it is still
        // Running, record it as Interrupted, and overwrite the user's cancel.
        cancellationRegistry.TryCancel(request.RunId);
        await cache.RemoveByTagAsync("runs", ct);

        return new CancelRunResultDto(run.Id, "cancelled");
    }
}
