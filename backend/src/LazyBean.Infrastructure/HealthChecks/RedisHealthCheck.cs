using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LazyBean.Infrastructure.HealthChecks;

// Only registered when a Redis connection string is configured (see DependencyInjection) -
// a round-trip set/get through the distributed cache is enough to prove connectivity
// without pulling in a direct StackExchange.Redis dependency here.
public class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    private const string ProbeKey = "healthcheck:redis:probe";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var value = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            await cache.SetAsync(ProbeKey, value, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            }, ct);

            var roundTripped = await cache.GetAsync(ProbeKey, ct);

            return roundTripped is not null
                ? HealthCheckResult.Healthy("Redis reachable.")
                : HealthCheckResult.Unhealthy("Redis round-trip did not return the probe value.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connectivity check threw.", ex);
        }
    }
}
