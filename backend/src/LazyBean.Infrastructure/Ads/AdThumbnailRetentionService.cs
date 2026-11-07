using LazyBean.Application.Ads.Common;
using LazyBean.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

// Removes the images of ads not seen for the retention period, once a day, and logs how much image data is still stored.
public sealed class AdThumbnailRetentionService(IServiceScopeFactory scopes, ILogger<AdThumbnailRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The first run comes a little after startup, so the start-up work is not slowed down.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var thumbnails = scope.ServiceProvider.GetRequiredService<IAdThumbnailService>();
                var result = await thumbnails.ApplyRetentionAsync(DateTimeOffset.UtcNow, stoppingToken);
                logger.LogInformation(
                    "Ad image retention: {Ads} ad(s) not seen for {Days} days lost their images ({Images} image(s) removed). Images stored now: {Mb:F1} MB.",
                    result.AdsCleared,
                    AdThumbnailRules.RetentionDays,
                    result.ImagesDeleted,
                    result.BytesStored / 1048576.0);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ad image retention failed. It runs again tomorrow.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
