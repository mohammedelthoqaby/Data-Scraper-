using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Application.Collector.Ports;

namespace LazyBean.Application.Collector.Orchestration;

// Downloads and uploads the images of the ads the server flagged (new, changed, or without images). Each image is read a
// second or two after the previous one, and at most MaxAdsPerRun ads are read in one run. A refusal from an image host stops
// the images for this run; the ads keep the images they have.
public sealed class AdThumbnailSync(
    ICollectorIngestApi api,
    IAdThumbnailProcessor processor,
    ICollectionLog log,
    Func<TimeSpan, CancellationToken, Task> delay)
{
    public const int MaxAdsPerRun = 100;
    public const int UploadBatchSize = 10;

    // The ad and its image addresses in card order: the first is the card image.
    public sealed record Candidate(string ExternalAdId, IReadOnlyList<string> ImageUrls);

    public async Task SyncAsync(string platform, string identityExternalId, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var batch = new List<AdThumbnailItem>();
        long bytes = 0;
        var adsRead = 0;
        var imagesSkipped = 0;
        try
        {
            foreach (var candidate in candidates.Take(MaxAdsPerRun))
            {
                ct.ThrowIfCancellationRequested();

                var sourceKey = AdThumbnailRules.SourceKey(candidate.ImageUrls.FirstOrDefault());
                if (sourceKey is null)
                {
                    continue;
                }

                var images = new List<string>();
                foreach (var url in candidate.ImageUrls.Take(AdThumbnailRules.MaxImagesPerAd))
                {
                    await delay(TimeSpan.FromSeconds(1 + Random.Shared.NextDouble()), ct);
                    var thumbnail = await processor.ReadThumbnailAsync(url, ct);
                    if (thumbnail is null)
                    {
                        imagesSkipped++;
                        continue;
                    }

                    images.Add(Convert.ToBase64String(thumbnail));
                    bytes += thumbnail.Length;
                }

                adsRead++;
                if (images.Count == 0)
                {
                    continue;
                }

                batch.Add(new AdThumbnailItem(candidate.ExternalAdId, sourceKey, images));
                if (batch.Count >= UploadBatchSize)
                {
                    await UploadAsync(platform, identityExternalId, batch, ct);
                }
            }
        }
        catch (CollectorTransportException ex)
        {
            log.Warn($"{platform}: an image host refused a thumbnail ({ex.Message}). Images stop for this run; the ads keep their images.");
        }

        await UploadAsync(platform, identityExternalId, batch, ct);
        log.Info($"{platform}: images read for {adsRead} ad(s), {bytes / 1024} KB, {imagesSkipped} image(s) skipped.");
    }

    private async Task UploadAsync(string platform, string identityExternalId, List<AdThumbnailItem> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            var response = await api.UploadAdThumbnailsAsync(
                new AdThumbnailUploadRequest(platform, identityExternalId, batch.ToList()), ct);
            var reason = response.Rejected.FirstOrDefault()?.Reason;
            log.Info($"{platform}: the server stored images for {response.Stored} ad(s) ({response.ImagesStored} image(s))"
                + (response.Rejected.Count > 0 ? $"; {response.Rejected.Count} refused. First reason: {reason}" : "."));
        }
        catch (CollectorApiException ex)
        {
            log.Warn($"{platform}: the server did not accept {batch.Count} ad image set(s). {ex.Message}");
        }
        finally
        {
            batch.Clear();
        }
    }
}
