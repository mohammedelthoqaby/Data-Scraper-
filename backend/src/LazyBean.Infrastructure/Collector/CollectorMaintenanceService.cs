using LazyBean.Application.Collector.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collector;

// Releases claims from apps that went quiet and skips jobs nobody picked up. Runs on its own timer
// so ads jobs are settled even after the run that created them has finished.
public sealed class CollectorMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<CollectorMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICollectorJobService>().MaintainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Collector job maintenance failed; it will retry in {Seconds}s.", Interval.TotalSeconds);
            }
        }
    }
}
