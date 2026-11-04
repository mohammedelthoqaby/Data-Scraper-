namespace LazyBean.Infrastructure.Collection.Http;

// Thrown once retries are exhausted - the orchestrator catches this and marks the
// CompetitorRun Failed rather than letting it bubble out of the whole collection run.
public class CollectionFetchException(string url, int? lastStatusCode, Exception? inner = null)
    : Exception($"Failed to fetch '{url}' after retries (last status: {lastStatusCode?.ToString() ?? "n/a"}).", inner)
{
    public string Url { get; } = url;
    public int? LastStatusCode { get; } = lastStatusCode;
}
