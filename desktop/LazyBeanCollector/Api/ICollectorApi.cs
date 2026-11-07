using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;

namespace LazyBean.Collector.App.Api;

// What the desktop app asks of the server. CollectorApiClient is the real implementation; tests use fakes.
public interface ICollectorApi
{
    // The device token used for every request after pairing. Null until the PC is paired.
    string? BearerToken { get; set; }

    Task<PairCollectorDeviceResultDto> PairAsync(string username, string password, string deviceName, CancellationToken ct);

    Task<CollectorJobDto?> ClaimNextJobAsync(CancellationToken ct);

    Task HeartbeatAsync(Guid jobId, CollectorHeartbeatRequest? progress, CancellationToken ct);

    // Gives the claim back when the app exits mid-job. The server returns the job to the queue.
    Task ReleaseJobAsync(Guid jobId, CancellationToken ct);

    Task CompleteJobAsync(Guid jobId, CompleteCollectorJobRequest request, CancellationToken ct);

    Task<CollectorIdentities> GetIdentitiesAsync(CancellationToken ct);

    Task IngestProductsAsync(Guid runId, string slug, IngestProductsBatchRequest request, CancellationToken ct);

    Task CompleteCompetitorAsync(Guid runId, string slug, CompleteCompetitorRunRequest request, CancellationToken ct);

    Task<AdsIngestResponse> IngestMetaAdsAsync(MetaAdsIngestRequest request, CancellationToken ct);

    Task<AdsIngestResponse> IngestGoogleAdsAsync(GoogleAdsIngestRequest request, CancellationToken ct);

    Task<GoogleCreativeDetailsResponse> IngestGoogleCreativeDetailsAsync(GoogleCreativeDetailsRequest request, CancellationToken ct);

    Task<AdThumbnailUploadResponse> UploadAdThumbnailsAsync(AdThumbnailUploadRequest request, CancellationToken ct);

    Task RequestAdsNowAsync(CancellationToken ct);

    Task UnpairAsync(CancellationToken ct);
}

public sealed record CollectorIdentities(
    IReadOnlyList<MetaIdentityDto> Meta,
    IReadOnlyList<GoogleIdentityDto> Google);
