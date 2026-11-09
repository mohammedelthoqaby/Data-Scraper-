using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Common.Interfaces;

public interface IAdThumbnailService
{
    Task<AdThumbnailUploadResponse> UploadAsync(AdThumbnailUploadRequest request, CancellationToken ct = default);

    // Null when the ad has no image at that position.
    Task<AdThumbnailImage?> GetImageAsync(Guid adId, int position, CancellationToken ct = default);

    // Removes the images of ads not seen for the retention period. Returns what was removed and what is still stored.
    Task<AdThumbnailRetentionResult> ApplyRetentionAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed record AdThumbnailImage(byte[] Data, string ContentType, string Sha256, DateTimeOffset UpdatedAt);

public sealed record AdThumbnailRetentionResult(int AdsCleared, int ImagesDeleted, long BytesStored);
