using System.Diagnostics;
using LazyBean.Infrastructure.Collection.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class SelfKeepAliveHostedService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<SelfKeepAliveHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (CollectionRunBackgroundService.IsRunActive)
            {
                try
                {
                    var publicBase = configuration["App:PublicBaseUrl"] ?? "http://localhost:5188";
                    var selfUrl = $"{publicBase.TrimEnd('/')}/health";

                    var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var resp = await client.GetAsync(selfUrl, stoppingToken);
                    var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    logger.LogInformation(
                        "Self keep-alive ping sent to {Url}: status {Status}. Active run working set: {WorkingSet} MB.",
                        selfUrl, (int)resp.StatusCode, wsMb);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Self keep-alive ping failed (app pool under load or port mismatch).");
                }
            }
        }
    }
}
