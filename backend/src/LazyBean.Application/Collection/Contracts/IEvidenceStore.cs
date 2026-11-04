namespace LazyBean.Application.Collection.Contracts;

// Persists the raw response body (gzip'd, on a volume) and records a FetchedResource row
// pointing at it - the evidence trail behind every normalized product/price.
public interface IEvidenceStore
{
    Task SaveAsync(
        Guid runId, Guid competitorId, string url, int httpStatus, string rawBody, CancellationToken ct);
}
