using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;

namespace LazyBean.Application.Collector.Ports;

// What the collection use cases need from the server. The desktop app implements it over HTTP (Infrastructure's
// CollectorApiClient); the request and response shapes are the ones the server's ingest endpoints already accept.
public interface ICollectorIngestApi
{
    Task<CollectorIdentities> GetIdentitiesAsync(CancellationToken ct);

    Task IngestProductsAsync(Guid runId, string slug, IngestProductsBatchRequest request, CancellationToken ct);

    Task CompleteCompetitorAsync(Guid runId, string slug, CompleteCompetitorRunRequest request, CancellationToken ct);

    Task<AdsIngestResponse> IngestMetaAdsAsync(MetaAdsIngestRequest request, CancellationToken ct);

    Task<AdsIngestResponse> IngestGoogleAdsAsync(GoogleAdsIngestRequest request, CancellationToken ct);

    Task<GoogleCreativeDetailsResponse> IngestGoogleCreativeDetailsAsync(GoogleCreativeDetailsRequest request, CancellationToken ct);

    Task<AdThumbnailUploadResponse> UploadAdThumbnailsAsync(AdThumbnailUploadRequest request, CancellationToken ct);
}

// Lines the collection use cases write for the person watching the app (the desktop's activity log).
public interface ICollectionLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

// The step a running job is on, for the progress the app shows.
public interface ICollectionProgress
{
    void UpdateJob(string message, int done, int? total);
}

// Reads one ad image as the thumbnail the server stores. Null when the address is not a plain image, the download fails, or
// no encoding fits. A refusal from the image host, or a failed download, is thrown as CollectorTransportException.
public interface IAdThumbnailProcessor
{
    Task<byte[]?> ReadThumbnailAsync(string imageUrl, CancellationToken ct);
}

// Finds a public Facebook page's numeric ID from its name. Null when the page cannot be read.
public interface IFacebookPageIdLookup
{
    Task<string?> FindPageIdAsync(string pageName, CancellationToken ct);
}
