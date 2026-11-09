using LazyBean.Application.Ads.Models;

namespace LazyBean.Application.Ads.Ports;

// Meta's ad library, as the page collection needs it. One session serves the paging of one page; the adapter keeps its
// cookies and tokens inside it and translates its failures into the collector exceptions.
public interface IMetaAdsSource
{
    IMetaSearchSession OpenSession();
}

public interface IMetaSearchSession : IDisposable
{
    Task<MetaSearchStepResult> SearchAsync(MetaSearchStepRequest request, CancellationToken ct);
}

// One search request of the paging: a page of results for a page ID, from a cursor (null for the first page).
public sealed record MetaSearchStepRequest(
    string Country,
    string ActiveStatus,
    IReadOnlyList<string> PageIds,
    int First,
    string SessionId,
    string CollationToken,
    string? Cursor,
    string SearchType);

// What one search request returned. The ads are already read into the application's model.
public sealed record MetaSearchStepResult(
    IReadOnlyList<MetaAdDto> Ads,
    string? NextCursor,
    bool RateLimited,
    bool SessionExpired,
    string? Error);
