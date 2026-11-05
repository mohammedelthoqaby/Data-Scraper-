using System.Diagnostics;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Matching;

public class MatchingBackgroundService(
    IMatchingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MatchingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MatchingBackgroundService started.");

        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            var sw = Stopwatch.StartNew();
            logger.LogInformation("MatchingBackgroundService: Starting matching job for RunId={RunId}...", job.RunId);

            try
            {
                // Enforce 10-minute timeout per phase
                using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                phaseCts.CancelAfter(TimeSpan.FromMinutes(10));

                using var scope = scopeFactory.CreateScope();
                var matcher = scope.ServiceProvider.GetRequiredService<IProductMatcher>();
                var triageService = scope.ServiceProvider.GetService<IAiTriageService>();
                var alertService = scope.ServiceProvider.GetService<IAlertService>();

                var result = await matcher.MatchAllAsync(job.RunId, phaseCts.Token);

                // Run AI triage
                if (triageService != null)
                {
                    try
                    {
                        await triageService.TriagePendingReviewsAsync(100, phaseCts.Token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "MatchingBackgroundService: AI triage failed or skipped: {Message}", ex.Message);
                    }
                }

                // Run Alert Sync
                if (alertService != null)
                {
                    try
                    {
                        await alertService.SyncAlertsAsync(phaseCts.Token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "MatchingBackgroundService: Alerts sync failed: {Message}", ex.Message);
                    }
                }

                sw.Stop();
                logger.LogInformation(
                    "MatchingBackgroundService: Finished matching job for RunId={RunId} in {ElapsedMs}ms. AutoAccepted={AutoAccepted}, ReviewItems={ReviewItems}.",
                    job.RunId, sw.ElapsedMilliseconds, result.AutoAcceptedCount, result.ReviewItemCount);

                job.Completion?.TrySetResult(result);
            }
            catch (OperationCanceledException ex) when (!stoppingToken.IsCancellationRequested)
            {
                sw.Stop();
                var msg = "Matching phase timed out after 10 minutes.";
                logger.LogError(ex, "MatchingBackgroundService: {Message} for RunId={RunId} after {ElapsedMs}ms.", msg, job.RunId, sw.ElapsedMilliseconds);
                job.Completion?.TrySetException(new TimeoutException(msg));
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "MatchingBackgroundService: Matching job failed for RunId={RunId} after {ElapsedMs}ms: {Message}", job.RunId, sw.ElapsedMilliseconds, ex.Message);
                job.Completion?.TrySetException(ex);
            }
        }
    }
}
