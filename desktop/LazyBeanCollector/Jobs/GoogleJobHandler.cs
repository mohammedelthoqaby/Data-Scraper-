using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Jobs;

// Collects the approved Google advertisers through the Ads Transparency pages, with a 3-5 s pause between
// requests. A block from Google stops the job at once: the server backs the identity off for 24 hours.
public sealed class GoogleJobHandler(
    ICollectorApi api,
    IGoogleAdsTransparencyClient client,
    ActivityLog log,
    CollectorState state,
    Func<TimeSpan, CancellationToken, Task> delay)
{
    private const int MaxPagesPerAdvertiser = 4;
    private const int MaxCreativesPerAdvertiser = 100;

    public async Task<JobOutcome> HandleAsync(CancellationToken ct)
    {
        var identities = (await api.GetIdentitiesAsync(ct)).Google;
        if (identities.Count == 0)
        {
            log.Info("No approved Google advertisers to collect.");
            return new JobOutcome("No approved Google advertisers.", []);
        }

        var pages = new List<CollectorPageReport>();
        var totalAds = 0;

        for (var index = 0; index < identities.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var identity = identities[index];
            state.UpdateJob($"Google advertiser {index + 1} of {identities.Count}: {identity.CompetitorName}", index, identities.Count);

            if (!identity.ExternalId.StartsWith("AR", StringComparison.OrdinalIgnoreCase))
            {
                // Only the server can store a resolved advertiser ID; discovery must run there first.
                log.Warn($"{identity.CompetitorName}: no Google advertiser ID yet. Run Discover Google Advertisers on the server.");
                pages.Add(new CollectorPageReport(identity.ExternalId, "Failed", 0, "No Google advertiser ID yet. Run Discover Google Advertisers on the server."));
                continue;
            }

            if (identity.BlockedUntil is { } blockedUntil && blockedUntil > DateTimeOffset.UtcNow)
            {
                log.Warn($"{identity.CompetitorName}: Google blocked this advertiser until {blockedUntil.LocalDateTime:g}. Skipped.");
                pages.Add(new CollectorPageReport(identity.ExternalId, "Blocked", 0, $"Blocked until {blockedUntil:u}"));
                continue;
            }

            try
            {
                await delay(Pause(), ct);
                var creatives = await CollectCreativesAsync(identity.ExternalId, ct);
                var items = creatives.Select(ToPayload).ToList();

                await api.IngestGoogleAdsAsync(new GoogleAdsIngestRequest(identity.ExternalId, "Success", null, null, items), ct);
                totalAds += items.Count;
                pages.Add(new CollectorPageReport(identity.ExternalId, "Success", items.Count, null));
                log.Info($"{identity.CompetitorName}: {items.Count} Google ad(s).");
            }
            catch (GoogleAdsBlockedException ex)
            {
                await api.IngestGoogleAdsAsync(new GoogleAdsIngestRequest(identity.ExternalId, "Blocked", null, ex.Message, null), ct);
                pages.Add(new CollectorPageReport(identity.ExternalId, "Blocked", 0, ex.Message));
                log.Warn($"Google blocked requests for {identity.CompetitorName}. Stopping Google collection for this run.");
                break;
            }
            catch (CollectorApiException ex) when (ex.Kind is ApiErrorKind.NotFound or ApiErrorKind.BadRequest)
            {
                log.Error($"{identity.CompetitorName}: the server did not accept the ads ({ex.Message}).");
                pages.Add(new CollectorPageReport(identity.ExternalId, "Failed", 0, ex.Message));
            }
        }

        var succeeded = pages.Count(p => p.Status == "Success");
        return new JobOutcome($"Google: {totalAds} ad(s) from {succeeded} of {identities.Count} advertiser(s).", pages);
    }

    private async Task<List<GoogleCreativeDto>> CollectCreativesAsync(string advertiserId, CancellationToken ct)
    {
        var creatives = new List<GoogleCreativeDto>();
        var page = await client.SearchCreativesAsync(advertiserId, null, ct);
        creatives.AddRange(page.Creatives);

        var pageCount = 1;
        while (!string.IsNullOrWhiteSpace(page.NextPageToken) &&
               creatives.Count < MaxCreativesPerAdvertiser &&
               pageCount < MaxPagesPerAdvertiser)
        {
            await delay(Pause(), ct);
            page = await client.SearchCreativesAsync(advertiserId, page.NextPageToken, ct);
            creatives.AddRange(page.Creatives);
            pageCount++;
        }

        return creatives;
    }

    internal static GoogleAdPayloadItem ToPayload(GoogleCreativeDto creative) => new(
        creative.ExternalAdId,
        creative.FormatName,
        creative.Title,
        creative.Body,
        creative.SnapshotUrl,
        creative.LinkUrl,
        creative.FirstSeen,
        creative.LastSeen,
        creative.RawJson);

    private static TimeSpan Pause() => TimeSpan.FromSeconds(3 + Random.Shared.NextDouble() * 2);
}
