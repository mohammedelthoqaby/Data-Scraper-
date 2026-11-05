namespace LazyBean.Application.Ads.Dtos;

public record GoogleCreativeDto(
    string ExternalAdId,
    string AdvertiserId,
    string? AdvertiserName,
    int FormatId,
    string? FormatName,
    string? Title,
    string? Body,
    string? LinkUrl,
    string? SnapshotUrl,
    string? ImageUrl,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    string? RawJson);

public record GoogleCreativesPageResult(
    IReadOnlyList<GoogleCreativeDto> Creatives,
    string? NextPageToken);

public record GoogleAdsSyncResult(
    bool Executed,
    string Reason,
    int AdvertisersProcessed,
    int AdsIngested,
    int CampaignsUpdated,
    IReadOnlyList<string> BlockedAdvertisers,
    IReadOnlyList<string> Errors);
