using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Collector.Ports;

namespace LazyBean.Infrastructure.External.LazyBeanApi;

// What the desktop app asks of the server: the job queue and the pairing (the app's own runtime), and the ingest calls the
// collection use cases make (inherited from ICollectorIngestApi). CollectorApiClient is the real implementation; tests use fakes.
public interface ICollectorApi : ICollectorIngestApi
{
    // The device token used for every request after pairing. Null until the PC is paired.
    string? BearerToken { get; set; }

    Task<PairCollectorDeviceResultDto> PairAsync(string username, string password, string deviceName, CancellationToken ct);

    Task<CollectorJobDto?> ClaimNextJobAsync(CancellationToken ct);

    Task HeartbeatAsync(Guid jobId, CollectorHeartbeatRequest? progress, CancellationToken ct);

    // Gives the claim back when the app exits mid-job. The server returns the job to the queue.
    Task ReleaseJobAsync(Guid jobId, CancellationToken ct);

    Task CompleteJobAsync(Guid jobId, CompleteCollectorJobRequest request, CancellationToken ct);

    Task RequestAdsNowAsync(CancellationToken ct);

    Task UnpairAsync(CancellationToken ct);
}
