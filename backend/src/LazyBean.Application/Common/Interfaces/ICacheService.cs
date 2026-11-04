namespace LazyBean.Application.Common.Interfaces;

// Abstraction over the hybrid (in-process L1 + distributed L2) cache so Application
// stays unaware of the concrete HybridCache/Redis implementation living in Infrastructure.
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    // Invalidates every cache entry written with this tag, in one call - this is the
    // primary invalidation mechanism; command handlers tag-invalidate rather than
    // tracking and removing individual keys.
    Task RemoveByTagAsync(string tag, CancellationToken ct = default);
}
