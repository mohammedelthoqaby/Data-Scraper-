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
            try
            {
                using var scope = scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<ICollectionRunExecutor>();
                await executor.ExecuteAsync(runId, runToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("CollectionRun {RunId} execution was cancelled.", runId);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
                    var run = await db.CollectionRuns.FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
                    if (run is not null && (run.Status == CollectionRunStatus.Running || run.Status == CollectionRunStatus.Queued))
                    {
                        run.Status = CollectionRunStatus.Cancelled;
                        run.FinishedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception dbEx)
                {
                    logger.LogError(dbEx, "Failed to update CollectionRun {RunId} status to Cancelled.", runId);
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
                IsRunActive = false;
                cancellationRegistry.Unregister(runId);
            }
        }
    }
}
