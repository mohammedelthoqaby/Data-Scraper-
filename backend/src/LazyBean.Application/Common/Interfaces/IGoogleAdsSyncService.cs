using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public interface IGoogleAdsSyncService
{
    Task<GoogleAdsSyncResult> SyncGoogleAdsAsync(bool force, CancellationToken ct = default);
    Task<GoogleAdsSyncResult?> SyncGoogleAdsIfDueAsync(CancellationToken ct = default);
}
