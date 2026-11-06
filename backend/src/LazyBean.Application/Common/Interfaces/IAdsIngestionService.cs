using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public interface IAdsIngestionService
{
    Task<AdsIngestResponse> IngestMetaPageAsync(MetaAdsIngestRequest request, CancellationToken ct = default);

    Task<AdsIngestResponse> IngestGoogleAdvertiserAsync(GoogleAdsIngestRequest request, CancellationToken ct = default);
}
