using LazyBean.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Hybrid;

namespace LazyBean.Infrastructure.Caching;

// Wraps the built-in HybridCache: an in-process L1 cache backed by a distributed L2
// (Redis, configured in DependencyInjection) with automatic stampede protection.
public class HybridCacheService(HybridCache cache) : ICacheService
{
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken ct = default)
    {
        var options = expiration is null
            ? null
            : new HybridCacheEntryOptions { Expiration = expiration, LocalCacheExpiration = expiration };

        return await cache.GetOrCreateAsync(
            key,
            factory,
            static async (f, token) => await f(token),
            options,
            tags,
            cancellationToken: ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        cache.RemoveAsync(key, ct).AsTask();

    public Task RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        cache.RemoveByTagAsync(tag, ct).AsTask();
}
