using System.Diagnostics;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Orchestration;

// Drains IRunQueue one run id at a time - ExecuteAsync is awaited fully before the next
// item is read, which is what actually guarantees "only one run at a time" at execution
// time (StartRunCommand's HasActiveRunAsync check guarantees it at creation time).
public class CollectionRunBackgroundService(
    IRunQueue queue,
    IServiceScopeFactory scopeFactory,
    IRunCancellationTokenRegistry cancellationRegistry,
    ILogger<CollectionRunBackgroundService> logger)
    : BackgroundService
{
    public static bool IsRunActive { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in queue.ReadAllAsync(stoppingToken))
        {
            var runToken = cancellationRegistry.Register(runId, stoppingToken);
            IsRunActive = true;

            using var memMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            var memMonitorTask = Task.Run(async () =>
            {
                while (!memMonitorCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), memMonitorCts.Token);
                        var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
                        var gcMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                        logger.LogInformation(
                            "Memory Monitor (Run {RunId}): WorkingSet = {WsMb:F1} MB, GC Total = {GcMb:F1} MB",
                            runId, wsMb, gcMb);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error in memory monitor loop.");
                    }
                }
            }, CancellationToken.None);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<ICollectionRunExecutor>();
                await executor.ExecuteAsync(runId, runToken);
            }
            catch (OperationCanceledException)
            {
                var isUserCancelled = cancellationRegistry.IsUserCancelled(runId);
                var targetStatus = isUserCancelled ? CollectionRunStatus.Cancelled : CollectionRunStatus.Interrupted;

                logger.LogWarning(
                    "CollectionRun {RunId} execution stopped due to cancellation. Reason: {Reason}, TargetStatus: {Status}",
                    runId,
                    isUserCancelled ? "User cancellation request" : "Host shutdown / app-pool recycle",
                    targetStatus);

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
                    var run = await db.CollectionRuns.FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
                    if (run is not null && (run.Status == CollectionRunStatus.Running || run.Status == CollectionRunStatus.Queued))
                    {
                        run.Status = targetStatus;
                        run.FinishedAt = DateTimeOffset.UtcNow;

                        // Also mark any running competitor runs as Interrupted
                        if (targetStatus == CollectionRunStatus.Interrupted)
                        {
                            var activeCompRuns = await db.CompetitorRuns
                                .Where(cr => cr.RunId == runId && cr.Status == CompetitorRunStatus.Running)
                                .ToListAsync(CancellationToken.None);

                            foreach (var cr in activeCompRuns)
                            {
                                cr.Status = CompetitorRunStatus.Interrupted;
                                cr.Errors = Math.Max(1, cr.Errors + 1);
                                cr.Coverage = RunCoverage.Partial;
                            }
                        }

                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception dbEx)
                {
                    logger.LogError(dbEx, "Failed to update CollectionRun {RunId} status to {Status}.", runId, targetStatus);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CollectionRun {RunId} failed outside the per-competitor try/catch.", runId);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
                    var run = await db.CollectionRuns.FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
                    if (run is not null && run.Status == CollectionRunStatus.Running)
                    {
                        run.Status = CollectionRunStatus.Failed;
                        run.FinishedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception dbEx)
                {
                    logger.LogError(dbEx, "Failed to update CollectionRun {RunId} status to Failed.", runId);
                }
            }
            finally
            {
                memMonitorCts.Cancel();
                try { await memMonitorTask; } catch { }
                IsRunActive = false;
                cancellationRegistry.Unregister(runId);
            }
        }
    }
}
