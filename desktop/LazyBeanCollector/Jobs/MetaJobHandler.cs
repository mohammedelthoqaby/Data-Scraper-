using System.Globalization;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collectors.Meta;

namespace LazyBean.Collector.App.Jobs;

// The Meta page collector, behind an interface so the handler can be tested without Facebook.
public interface IMetaPageCollector
{
    Task<MetaPageResult> CollectPageAsync(string pageId, string country, string status, int maxResults, CancellationToken ct);
}

public sealed class MetaPageCollector : IMetaPageCollector
{
    public Task<MetaPageResult> CollectPageAsync(string pageId, string country, string status, int maxResults, CancellationToken ct) =>
        new MetaAdsPageCollector().CollectPageAsync(pageId, country, status, maxResults, ct);
}

// Collects every approved Meta page, one at a time, with a polite 20-40 s pause between pages (the same
// window the Python collector used). A page that Meta blocks is reported as Blocked and the server backs off.
public sealed class MetaJobHandler(
    ICollectorApi api,
    IMetaPageCollector collector,
    ActivityLog log,
    CollectorState state,
    Func<TimeSpan, CancellationToken, Task> delay,
    AdThumbnailSync? thumbnails = null)
{
    public const int MaxAdsPerPage = 50;

    public async Task<JobOutcome> HandleAsync(CancellationToken ct)
    {
        var identities = (await api.GetIdentitiesAsync(ct)).Meta;
        if (identities.Count == 0)
        {
            log.Info("No approved Meta pages to collect.");
            return new JobOutcome("No approved Meta pages.", []);
        }

        var pages = new List<CollectorPageReport>();
        var totalAds = 0;
        var madeRequest = false;

        for (var index = 0; index < identities.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var identity = identities[index];
            state.UpdateJob($"Meta page {index + 1}/{identities.Count}: {identity.CompetitorName}", index + 1, identities.Count);

            if (identity.BlockedUntil is { } blockedUntil && blockedUntil > DateTimeOffset.UtcNow)
            {
                log.Warn($"{identity.CompetitorName}: Meta blocked this page until {blockedUntil.LocalDateTime:g}. Skipped.");
                pages.Add(new CollectorPageReport(identity.PageId, "Blocked", 0, $"Blocked until {blockedUntil:u}"));
                continue;
            }

            if (madeRequest)
            {
                await delay(RandomPause(20, 40), ct);
            }

            madeRequest = true;
            var result = await collector.CollectPageAsync(identity.PageId, "EG", "ALL", MaxAdsPerPage, ct);

            var request = new MetaAdsIngestRequest(
                identity.PageId,
                result.Status,
                result.Error,
                result.Ads.Select(ToItem).ToList());

            try
            {
                var ingest = await api.IngestMetaAdsAsync(request, ct);

                // The images of the ads the server flagged. A carousel's images are kept in card order.
                if (thumbnails is not null)
                {
                    var wanted = (ingest.Items ?? []).Where(i => i.NeedsThumbnail).Select(i => i.ExternalAdId).ToHashSet(StringComparer.Ordinal);
                    var candidates = result.Ads
                        .Where(a => wanted.Contains(a.Id))
                        .Select(a => new AdThumbnailSync.Candidate(
                            a.Id,
                            a.ImageUrls ?? (a.ImageUrl is null ? Array.Empty<string>() : new[] { a.ImageUrl })))
                        .Where(c => c.ImageUrls.Count > 0)
                        .ToList();
                    await thumbnails.SyncAsync("Meta", identity.PageId, candidates, ct);
                }
            }
            catch (CollectorApiException ex) when (ex.Kind is ApiErrorKind.NotFound or ApiErrorKind.BadRequest)
            {
                log.Error($"{identity.CompetitorName}: the server did not accept the ads ({ex.Message}).");
                pages.Add(new CollectorPageReport(identity.PageId, "Failed", result.TotalCollected, ex.Message));
                continue;
            }

            totalAds += result.TotalCollected;
            pages.Add(new CollectorPageReport(identity.PageId, result.Status, result.TotalCollected, result.Error));
            log.Info($"{identity.CompetitorName}: {result.Status}, {result.TotalCollected} ad(s).{(result.Error is null ? string.Empty : " " + result.Error)}");
        }

        var succeeded = pages.Count(p => p.Status == "Success");
        var summary = $"Meta: {totalAds} ad(s) from {succeeded} of {identities.Count} page(s).";
        return new JobOutcome(summary, pages);
    }

    internal static MetaAdItemRequest ToItem(MetaAdDto ad) => new(
        ad.Id,
        ad.Title,
        ad.Body,
        ad.Link,
        ad.SnapshotUrl,
        ad.ImageUrl,
        ParseDate(ad.StartDate),
        ParseDate(ad.StopDate),
        ad.Status,
        null);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static TimeSpan RandomPause(int minSeconds, int maxSeconds) =>
        TimeSpan.FromSeconds(minSeconds + Random.Shared.NextDouble() * (maxSeconds - minSeconds));
}
