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

    // A server error on the upload is retried once, after this wait, with the same ads.
    private static readonly TimeSpan IngestRetryDelay = TimeSpan.FromSeconds(30);

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
            state.UpdateJob($"Google advertiser {index + 1}/{identities.Count}: {identity.CompetitorName}", index + 1, identities.Count);

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

            var items = new List<GoogleAdPayloadItem>();
            try
            {
                await delay(Pause(), ct);
                var creatives = await CollectCreativesAsync(identity.ExternalId, ct);
                items = creatives.Select(ToPayload).ToList();

                await UploadAsync(identity, items, ct);
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
            catch (CollectorApiException ex) when (ex.Kind is ApiErrorKind.NotFound or ApiErrorKind.BadRequest or ApiErrorKind.ServerError)
            {
                // The server's own message is kept. After a server error the collected ads were not stored.
                var message = ex.Kind == ApiErrorKind.ServerError && items.Count > 0
                    ? $"{ex.Message} {items.Count} collected ad(s) were not saved."
                    : ex.Message;
                log.Error($"{identity.CompetitorName}: the server did not accept the ads. {message}");
                pages.Add(new CollectorPageReport(identity.ExternalId, "Failed", 0, message));
            }
        }

        var succeeded = pages.Count(p => p.Status == "Success");
        return new JobOutcome($"Google: {totalAds} ad(s) from {succeeded} of {identities.Count} advertiser(s).", pages);
    }

    // Uploads the ads. A server error is retried once, after 30 seconds, with the same ads kept in memory. Any other
    // failure goes to the caller at once.
    private async Task UploadAsync(GoogleIdentityDto identity, List<GoogleAdPayloadItem> items, CancellationToken ct)
    {
        var request = new GoogleAdsIngestRequest(identity.ExternalId, "Success", null, null, items);
        AdsIngestResponse response;
        try
        {
            response = await api.IngestGoogleAdsAsync(request, ct);
        }
        catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.ServerError)
        {
            log.Warn($"{identity.CompetitorName}: the server could not save {items.Count} Google ad(s) ({ex.Message}). Retrying once in 30 seconds.");
            await delay(IngestRetryDelay, ct);
            response = await api.IngestGoogleAdsAsync(request, ct);
        }

        var skipped = response.Items?.Where(item => item.Outcome == "skipped").ToList() ?? [];
        if (skipped.Count > 0)
        {
            log.Warn($"{identity.CompetitorName}: the server skipped {skipped.Count} Google ad(s). First reason: {skipped[0].Reason}");
        }
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
