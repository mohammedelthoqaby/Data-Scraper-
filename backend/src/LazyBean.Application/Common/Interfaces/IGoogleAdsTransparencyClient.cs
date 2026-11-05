using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public interface IGoogleAdsTransparencyClient
{
    Task<string?> SearchAdvertiserIdAsync(string query, CancellationToken ct = default);
    Task<GoogleCreativesPageResult> SearchCreativesAsync(string advertiserId, string? pageToken = null, CancellationToken ct = default);
}
