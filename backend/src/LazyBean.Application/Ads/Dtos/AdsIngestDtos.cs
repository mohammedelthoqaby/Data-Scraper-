using System.Text.Json.Serialization;

namespace LazyBean.Application.Ads.Dtos;

public sealed record GoogleAdPayloadItem(
    string ExternalAdId,
    string? Format,
    string? Title,
    string? Body,
    string? SnapshotUrl,
    string? LinkUrl,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    string? RawJson,
    string? PreviewUrl = null);

public sealed record GoogleAdsIngestRequest(
    string AdvertiserId,
    string? Status,
    string? Region,
    string? Error,
    IReadOnlyList<GoogleAdPayloadItem>? Ads);

public sealed record GoogleIdentityDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    string ExternalId,
    string Name,
    string Status,
    DateTimeOffset? BlockedUntil);

public sealed record MetaIdentityDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    string PageId,
    string Name,
    string Status,
    DateTimeOffset? BlockedUntil);

public sealed record MetaAdItemRequest(
    string Id,
    string? Title,
    string? Body,
    string? Link,
    string? SnapshotUrl,
    string? ImageUrl,
    DateTimeOffset? StartDate,
    DateTimeOffset? StopDate,
    string? Status,
    string? RawJson);

public sealed record MetaAdsIngestRequest(
    string PageId,
    string Status,
    string? Error,
    List<MetaAdItemRequest>? Ads);

// Response body of the ads ingest endpoints. Only the fields that apply to an outcome are written.
public sealed record AdsIngestResponse(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? IngestedCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalProcessed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AdsIngestItemResult>? Items = null);

// Outcome of one creative in a Google ads batch: created, updated, duplicate (repeated in the batch) or skipped
// (with the reason). Reasons are short and safe to show in the desktop app.
// NeedsDetail: the creative's detail should be read now (it was never read, or the listing shows it again since).
public sealed record AdsIngestItemResult(string ExternalAdId, string Outcome, string? Reason = null, bool NeedsDetail = false);
