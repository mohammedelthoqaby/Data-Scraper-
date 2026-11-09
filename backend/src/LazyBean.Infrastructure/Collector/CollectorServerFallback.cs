using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collector;

// Google ads only: when no desktop app is connected, the server runs the Google sync itself so a
// run still gets Google data. It runs in the background so the run never waits on it, and it
// records its outcome on the job like any other source.
public sealed class CollectorServerFallback(
    IServiceScopeFactory scopeFactory,
    ILogger<CollectorServerFallback> logger) : ICollectorServerFallback
{
    public void ScheduleGoogleFallback(Guid jobId)
    {
        _ = Task.Run(() => RunGoogleAsync(jobId));
    }

    private async Task RunGoogleAsync(Guid jobId)
    {
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var sync = scope.ServiceProvider.GetRequiredService<IGoogleAdsSyncService>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

        try
        {
            var result = await sync.SyncGoogleAdsAsync(force: true, CancellationToken.None);
            var summary =
                $"Completed by server fallback: {result.AdsIngested} ad(s) from {result.AdvertisersProcessed} advertiser(s).";
            if (result.Errors.Count > 0)
            {
                summary += $" {result.Errors.Count} error(s).";
            }

            await CompleteAsync(unitOfWork, jobId, job => job.Complete(clock.GetUtcNow(), summary, null));
            await alerts.SyncAlertsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Server Google fallback failed for collector job {JobId}.", jobId);
            await CompleteAsync(
                unitOfWork,
                jobId,
                job => job.Fail(clock.GetUtcNow(), $"Server fallback failed: {ex.Message}", null));
        }
    }

    private static async Task CompleteAsync(
        IUnitOfWork unitOfWork,
        Guid jobId,
        Action<Domain.Entities.CollectorJob> transition)
    {
        var job = await unitOfWork.CollectorJobs.GetByIdAsync(jobId);
        if (job is null || job.IsTerminal)
        {
            return;
        }

        transition(job);
        unitOfWork.CollectorJobs.Update(job);
        await unitOfWork.SaveChangesAsync();
    }
}
