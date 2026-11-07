namespace LazyBean.Application.Ads.Dtos;

// The images of one creative, as base64 JPEG bytes in card order (at most AdThumbnailRules.MaxImagesPerAd).
// SourceKey is the image address without its query string, so a changed creative is recognised on the next run.
public sealed record AdThumbnailItem(string ExternalAdId, string SourceKey, IReadOnlyList<string> ImagesBase64);

public sealed record AdThumbnailUploadRequest(string Platform, string IdentityExternalId, IReadOnlyList<AdThumbnailItem> Items);

public sealed record AdThumbnailRejection(string ExternalAdId, string Reason);

public sealed record AdThumbnailUploadResponse(int Stored, int ImagesStored, IReadOnlyList<AdThumbnailRejection> Rejected);
