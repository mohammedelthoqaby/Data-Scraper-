using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Jobs;

// Collects the approved Google advertisers through the Ads Transparency pages, with a 3-5 s pause between
// requests. A block from Google stops the job at once: the server backs the identity off for 24 hours.
//
// After each advertiser's creatives are uploaded, the server says which creatives still need their detail (never read, or
// shown again since). Those details are read the same way, a few seconds apart, and sent back in batches. A block while
// reading details stops the detail reads and the rest of the run, but does not back the advertiser off: its ads are saved.
public sealed class GoogleJobHandler(
    ICollectorApi api,
    IGoogleAdsTransparencyClient client,
    ActivityLog log,
    CollectorState state,
    Func<TimeSpan, CancellationToken, Task> delay)
{
    private const int MaxPagesPerAdvertiser = 4;
    private const int MaxCreativesPerAdvertiser = 100;
    private const int DetailBatchSize = 20;

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
            var detailsBlocked = false;
            try
            {
                await delay(Pause(), ct);
                var creatives = await CollectCreativesAsync(identity.ExternalId, ct);
                // A creative listed on two result pages is sent once; the first copy is kept.
                var unique = creatives.GroupBy(c => c.ExternalAdId).Select(g => g.First()).ToList();
                items = unique.Select(ToPayload).ToList();
                log.Info($"{identity.CompetitorName}: {creatives.Count} creative(s) collected, {unique.Count} unique, uploading now.");

                var needDetail = await UploadAsync(identity, items, ct);
                totalAds += items.Count;
                detailsBlocked = await FetchDetailsAsync(identity, needDetail, ct);
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

            if (detailsBlocked)
            {
                log.Warn("Google blocked creative detail requests. Stopping Google collection for this run; the details wait for a later run.");
                break;
            }
        }

        var succeeded = pages.Count(p => p.Status == "Success");
        return new JobOutcome($"Google: {totalAds} ad(s) from {succeeded} of {identities.Count} advertiser(s).", pages);
    }

    // Uploads the ads. A server error is retried once, after 30 seconds, with the same ads kept in memory. Any other
    // failure goes to the caller at once. Returns the creative IDs whose details the server asks for.
    private async Task<IReadOnlyList<string>> UploadAsync(GoogleIdentityDto identity, List<GoogleAdPayloadItem> items, CancellationToken ct)
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

        var results = response.Items ?? [];
        var created = results.Count(item => item.Outcome == "created");
        var updated = results.Count(item => item.Outcome == "updated");
        var duplicate = results.Count(item => item.Outcome == "duplicate");
        var skipped = results.Where(item => item.Outcome == "skipped").ToList();
        log.Info($"{identity.CompetitorName}: uploaded {items.Count}; the server created {created}, updated {updated}, "
            + $"skipped {skipped.Count}, duplicate {duplicate}.");
        if (skipped.Count > 0)
        {
            log.Warn($"{identity.CompetitorName}: the server skipped {skipped.Count} Google ad(s). First reason: {skipped[0].Reason}");
        }

        return results.Where(item => item.NeedsDetail && item.Outcome is "created" or "updated")
            .Select(item => item.ExternalAdId)
            .ToList();
    }

    // Reads the detail of each creative the server asked for, a few seconds apart, and sends them in batches. Returns true
    // when Google blocked the detail reads: the detail reads then stop, and the caller stops the run.
    private async Task<bool> FetchDetailsAsync(GoogleIdentityDto identity, IReadOnlyList<string> creativeIds, CancellationToken ct)
    {
        if (creativeIds.Count == 0)
        {
            return false;
        }

        var batch = new List<GoogleCreativeDetailDto>();
        var read = 0;
        foreach (var creativeId in creativeIds.Take(MaxCreativesPerAdvertiser))
        {
            ct.ThrowIfCancellationRequested();
            await delay(Pause(), ct);

            try
            {
                var detail = await client.GetCreativeDetailAsync(identity.ExternalId, creativeId, ct);
                read++;
                if (detail is not null)
                {
                    batch.Add(detail);
                }
            }
            catch (GoogleAdsBlockedException ex)
            {
                log.Warn($"{identity.CompetitorName}: Google blocked a creative detail after {read} read. {ex.Message}");
                await SendDetailsAsync(identity, batch, ct);
                return true;
            }
            catch (HttpRequestException ex)
            {
                log.Warn($"{identity.CompetitorName}: creative {creativeId} could not be read ({ex.Message}). Skipped.");
                continue;
            }

            if (batch.Count >= DetailBatchSize)
            {
                await SendDetailsAsync(identity, batch, ct);
                batch.Clear();
            }
        }

        await SendDetailsAsync(identity, batch, ct);
        log.Info($"{identity.CompetitorName}: creative details read for {read} creative(s).");
        return false;
    }

    // A batch the server does not accept is logged and dropped. The creatives keep their missing detail, so the next run reads them again.
    private async Task SendDetailsAsync(GoogleIdentityDto identity, List<GoogleCreativeDetailDto> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            var response = await api.IngestGoogleCreativeDetailsAsync(
                new GoogleCreativeDetailsRequest(identity.ExternalId, batch.ToList()), ct);
            log.Info($"{identity.CompetitorName}: the server stored {response.Updated} creative detail(s); {response.NotFound} not found.");
        }
        catch (CollectorApiException ex)
        {
            log.Warn($"{identity.CompetitorName}: the server did not accept {batch.Count} creative detail(s). {ex.Message}");
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
        creative.RawJson,
        creative.ImageUrl);

    private static TimeSpan Pause() => TimeSpan.FromSeconds(3 + Random.Shared.NextDouble() * 2);
}
