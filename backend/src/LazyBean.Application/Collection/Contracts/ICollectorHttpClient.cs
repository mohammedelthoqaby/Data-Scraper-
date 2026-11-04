using LazyBean.Domain.Entities;

namespace LazyBean.Application.Collection.Contracts;

// The one place every outbound request goes through: fixed User-Agent, per-competitor
// delay between requests to the same host, robots.txt enforcement, and 429/503 retry
// with Retry-After honored. Adapters never call HttpClient directly.
public interface ICollectorHttpClient
{
    Task<CollectedResponse> GetAsync(Competitor competitor, string url, CancellationToken ct);
}

public sealed record CollectedResponse(
    int StatusCode,
    string Body,
    bool Success,
    string? SkippedReason = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public string? GetHeader(string name) =>
        Headers?.TryGetValue(name, out var value) == true ? value : null;
}
