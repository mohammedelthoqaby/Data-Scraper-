using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LazyBean.Infrastructure.Collection.Scheduling;

// Fired by the "DailyCollection" trigger (07:00 Africa/Cairo - see DependencyInjection).
// If a run is already in flight (manual or a previous scheduled one still going), this
// just logs and does nothing - "only one run at a time" wins over "run on schedule."
[DisallowConcurrentExecution]
public class DailyCollectionJob(ISender sender, ILogger<DailyCollectionJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        try
        {
            var runId = await sender.Send(new StartRunCommand(RunTrigger.Scheduled), ct);
            logger.LogInformation("Scheduled collection run {RunId} queued.", runId);
        }
        catch (RunAlreadyInProgressException)
        {
            logger.LogInformation("Scheduled collection run skipped - a run is already queued or running.");
        }
    }
}
