using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public record GoogleAdvertiserCandidateDto(
    string AdvertiserId,
    string Name,
    string? Region,
    int? ApproxAdCount);

public interface IGoogleAdsTransparencyClient
{
    Task<string?> SearchAdvertiserIdAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<GoogleAdvertiserCandidateDto>> SearchCandidatesAsync(string query, CancellationToken ct = default);
    Task<GoogleCreativesPageResult> SearchCreativesAsync(string advertiserId, string? pageToken = null, CancellationToken ct = default);
}
