using LazyBean.Application.Collection.Contracts;

namespace LazyBean.Collector.App.Runtime;

// The shared HTTP policy saves raw pages as evidence on the server. The desktop app keeps none: the
// products it sends are the evidence, and raw pages would only fill the PC's disk.
public sealed class NoEvidenceStore : IEvidenceStore
{
    public Task SaveAsync(Guid runId, Guid competitorId, string url, int httpStatus, string rawBody, CancellationToken ct) =>
        Task.CompletedTask;
}
