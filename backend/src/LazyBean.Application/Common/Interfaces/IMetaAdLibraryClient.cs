namespace LazyBean.Application.Common.Interfaces;

public record MetaTokenDebugInfo(
    bool IsValid,
    DateTimeOffset? ExpiresAt,
    string? AppId,
    IReadOnlyList<string> Scopes,
    string? ErrorMessage);

public record MetaAdRawItem(
    string Id,
    string? Title,
    string? Body,
    string? LinkUrl,
    string? SnapshotUrl,
    DateTimeOffset? DeliveryStartTime,
    DateTimeOffset? DeliveryStopTime,
    string? RawJson);

public interface IMetaAdLibraryClient
{
    Task<MetaTokenDebugInfo> DebugTokenAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MetaAdRawItem>> FetchAdsAsync(string pageId, CancellationToken ct = default);
}
