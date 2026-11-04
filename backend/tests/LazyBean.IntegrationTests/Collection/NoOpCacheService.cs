using LazyBean.Application.Common.Interfaces;

namespace LazyBean.IntegrationTests.Collection;

// The orchestrator calls RemoveByTagAsync at the end of a run - this test doesn't care
// about caching, just that the call doesn't throw.
internal class NoOpCacheService : ICacheService
{
    public Task<T> GetOrCreateAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null, CancellationToken ct = default) => factory(ct);

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveByTagAsync(string tag, CancellationToken ct = default) => Task.CompletedTask;
}
