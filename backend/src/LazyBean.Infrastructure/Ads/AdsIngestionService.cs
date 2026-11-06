using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LazyBean.Infrastructure.Ads;

// Ads ingestion shared by the internal API and the paired desktop collector. Each creative is stored under its
// advertiser account, so one account's creatives are never replaced by another's. A Google advertiser has one
// campaign that groups its creatives. A Meta creative is grouped by its body text. Every ingest records a batch with
// the counts the server did, so the Ads summary can reconcile against the app.
public class AdsIngestionService(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    LazyBean.Application.Ads.Common.CreativeVisionService creativeVision,
    ILogger<AdsIngestionService> logger) : IAdsIngestionService
{
    public async Task<AdsIngestResponse> IngestGoogleAdvertiserAsync(GoogleAdsIngestRequest request, CancellationToken ct = default)
    {
        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Google && i.ExternalId == request.AdvertiserId, ct);

        if (identity == null)
        {
            throw new NotFoundException(nameof(AdvertiserIdentity), request.AdvertiserId);
        }

        var now = DateTimeOffset.UtcNow;

        if (string.Equals(request.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            var blockedReason = request.Error ?? "Google Ads Transparency blocked/rate-limited direct scraper.";
            identity.Block(now.AddHours(24), blockedReason);
            unitOfWork.AdvertiserIdentities.Update(identity);

            var existingGoogleAds = await readContext.Query<Ad>()
                .Where(a => a.IdentityId == identity.Id)
                .ToListAsync(ct);

            if (existingGoogleAds.Count == 0)
            {
                var blockedAd = new Ad
                {
                    CompetitorId = identity.CompetitorId,
                    IdentityId = identity.Id,
                    ExternalAdId = $"{identity.ExternalId}_BLOCKED",
                    Platform = AdPlatform.Google,
                    Title = $"{identity.Name} (Google Ads Blocked)",
                    Body = "Google Ads Transparency rate-limited or blocked direct access.",
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    Status = AdStatus.Blocked,
                    CreativeHash = "blocked"
                };
                await unitOfWork.Ads.AddAsync(blockedAd, ct);
            }
            else
            {
                foreach (var ega in existingGoogleAds)
                {
                    ega.Status = AdStatus.Blocked;
                    ega.LastSeenAt = now;
                    unitOfWork.Ads.Update(ega);
                }
            }

            await RecordBatchAsync(identity, 0, 0, 0, 0, 0, "Blocked", blockedReason, now, ct);
            logger.LogWarning("Marked Google advertiser identity '{Name}' as Blocked for 24h.", identity.Name);
            return new AdsIngestResponse("Blocked", Message: "Advertiser identity marked Blocked for 24h.");
        }

        var results = new List<AdsIngestItemResult>();
        var received = request.Ads?.Count ?? 0;

        if (request.Ads is { Count: > 0 })
        {
            var accepted = new List<GoogleAdNormalized>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawAd in request.Ads)
            {
                var normalized = GoogleAdNormalizer.Normalize(rawAd, now);
                if (normalized.Ad is null)
                {
                    results.Add(new AdsIngestItemResult(normalized.ExternalAdId ?? string.Empty, "skipped", normalized.SkipReason));
                    logger.LogWarning(
                        "Skipped a Google creative for '{Name}' ({AdvertiserId}): {Reason}",
                        identity.Name, identity.ExternalId, normalized.SkipReason);
                    continue;
                }

                if (!seenIds.Add(normalized.Ad.ExternalAdId))
                {
                    results.Add(new AdsIngestItemResult(
                        normalized.Ad.ExternalAdId, "duplicate", "Listed more than once in this batch; the first copy was used."));
                    continue;
                }

                accepted.Add(normalized.Ad);
            }

            var catalog = await LoadCatalogAsync(identity.CompetitorId, ct);
            var campaign = await GetOrCreateGoogleCampaignAsync(identity, now, ct);

            // The existing creatives of this advertiser account only: the same creative ID under another account is
            // a different row.
            var acceptedIds = accepted.Select(a => a.ExternalAdId).ToList();
            var existingAds = acceptedIds.Count == 0
                ? new List<Ad>()
                : await readContext.Query<Ad>()
                    .Where(a => a.IdentityId == identity.Id && a.Platform == AdPlatform.Google && acceptedIds.Contains(a.ExternalAdId))
                    .ToListAsync(ct);

            var existingByExternalId = new Dictionary<string, Ad>(StringComparer.Ordinal);
            foreach (var existing in existingAds)
            {
                existingByExternalId.TryAdd(existing.ExternalAdId, existing);
            }

            // The creatives saved in this batch: the optional AI-read may read them after the loop.
            var touched = new List<Ad>();

            // Each creative is saved on its own, so one rejected creative cannot fail the whole batch.
            foreach (var ad in accepted)
            {
                try
                {
                    campaign.LastSeenAt = now;
                    unitOfWork.AdCampaigns.Update(campaign);

                    string outcome;
                    Ad saved;
                    if (existingByExternalId.TryGetValue(ad.ExternalAdId, out var existingAd))
                    {
                        existingAd.LastSeenAt = ad.LastSeen ?? now;
                        existingAd.DeliveryStopDate = ad.LastSeen;
                        existingAd.CampaignId = campaign.Id;
                        existingAd.Status = AdStatus.Active;
                        existingAd.PreviewUrl = ad.PreviewUrl ?? existingAd.PreviewUrl;
                        existingAd.CreativeFormat = ad.CreativeFormat ?? existingAd.CreativeFormat;
                        AdDerivedFields.Apply(existingAd, catalog);
                        unitOfWork.Ads.Update(existingAd);
                        outcome = "updated";
                        saved = existingAd;
                    }
                    else
                    {
                        var newAd = new Ad
                        {
                            CompetitorId = identity.CompetitorId,
                            IdentityId = identity.Id,
                            CampaignId = campaign.Id,
                            ExternalAdId = ad.ExternalAdId,
                            Platform = AdPlatform.Google,
                            Title = ad.Title ?? identity.Name,
                            Body = ad.Body,
                            LinkUrl = ad.LinkUrl,
                            SnapshotUrl = ad.SnapshotUrl,
                            PreviewUrl = ad.PreviewUrl,
                            CreativeFormat = ad.CreativeFormat,
                            DeliveryStartDate = ad.FirstSeen,
                            DeliveryStopDate = ad.LastSeen,
                            FirstSeenAt = ad.FirstSeen ?? now,
                            LastSeenAt = ad.LastSeen ?? now,
                            Status = AdStatus.Active,
                            CreativeHash = CreativeHashHelper.Compute(ad.Title, ad.Body, ad.SnapshotUrl),
                            RawDataJson = ad.RawJson
                        };
                        AdDerivedFields.Apply(newAd, catalog);
                        await unitOfWork.Ads.AddAsync(newAd, ct);
                        outcome = "created";
                        saved = newAd;
                    }

                    await unitOfWork.SaveChangesAsync(ct);
                    touched.Add(saved);
                    results.Add(new AdsIngestItemResult(ad.ExternalAdId, outcome, null, GoogleCreativeDetailApplier.NeedsDetail(saved)));
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException)
                {
                    // Drop the rejected creative's pending changes, so the next creative starts from a clean tracker.
                    unitOfWork.ClearTracker();
                    var reason = DescribeRejection(ex);
                    results.Add(new AdsIngestItemResult(ad.ExternalAdId, "skipped", reason));
                    logger.LogWarning(ex, "Skipped Google creative {ExternalAdId} for '{Name}': {Reason}", ad.ExternalAdId, identity.Name, reason);
                }
            }

            // The optional AI-read runs after the ads are saved. If it fails, the saved ads stay as they are.
            if (touched.Count > 0)
            {
                try
                {
                    await creativeVision.ApplyAsync(touched, catalog, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unitOfWork.ClearTracker();
                    logger.LogWarning(ex, "The AI-read step failed for '{Name}'. The ads were saved without it.", identity.Name);
                }
            }
        }

        var createdCount = results.Count(r => r.Outcome == "created");
        var updatedCount = results.Count(r => r.Outcome == "updated");
        var duplicateCount = results.Count(r => r.Outcome == "duplicate");
        var skippedCount = results.Count(r => r.Outcome == "skipped");

        await RecordBatchAsync(identity, received, createdCount, updatedCount, duplicateCount, skippedCount, "Success", null, now, ct);
        logger.LogInformation(
            "Google ads from '{Name}' ({AdvertiserId}): received {Received}, created {Created}, updated {Updated}, duplicate {Duplicate}, skipped {Skipped}.",
            identity.Name, identity.ExternalId, received, createdCount, updatedCount, duplicateCount, skippedCount);

        return new AdsIngestResponse(
            "Success",
            Message: skippedCount == 0 ? null : $"{skippedCount} creative(s) skipped; see items for the reason.",
            IngestedCount: createdCount,
            Items: results);
    }

    // Stores the details the desktop app read for one advertiser's creatives. A creative the server does not hold for that
    // advertiser is counted as NotFound. Each stored creative is re-linked from its copy and its landing link.
    public async Task<GoogleCreativeDetailsResponse> ApplyGoogleCreativeDetailsAsync(GoogleCreativeDetailsRequest request, CancellationToken ct = default)
    {
        const int maxItems = 100;
        var items = request.Items.Take(maxItems).ToList();

        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Google && i.ExternalId == request.AdvertiserId, ct);
        if (identity is null)
        {
            return new GoogleCreativeDetailsResponse(0, items.Count);
        }

        var ids = items.Select(i => i.ExternalAdId).Distinct(StringComparer.Ordinal).ToList();
        var ads = ids.Count == 0
            ? new List<Ad>()
            : await readContext.Query<Ad>()
                .Where(a => a.IdentityId == identity.Id && a.Platform == AdPlatform.Google && ids.Contains(a.ExternalAdId))
                .ToListAsync(ct);
        var byExternalId = new Dictionary<string, Ad>(StringComparer.Ordinal);
        foreach (var ad in ads)
        {
            byExternalId.TryAdd(ad.ExternalAdId, ad);
        }

        var catalog = await LoadCatalogAsync(identity.CompetitorId, ct);
        var now = DateTimeOffset.UtcNow;
        var updated = 0;
        var notFound = 0;
        foreach (var item in items)
        {
            if (!byExternalId.TryGetValue(item.ExternalAdId, out var ad))
            {
                notFound++;
                continue;
            }

            GoogleCreativeDetailApplier.Apply(ad, item, now, catalog);
            unitOfWork.Ads.Update(ad);
            updated++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Google creative details for '{Name}' ({AdvertiserId}): stored {Updated}, not found {NotFound}.",
            identity.Name, identity.ExternalId, updated, notFound);

        return new GoogleCreativeDetailsResponse(updated, notFound);
    }

    public async Task<AdsIngestResponse> IngestMetaPageAsync(MetaAdsIngestRequest request, CancellationToken ct = default)
    {
        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Meta && i.ExternalId == request.PageId, ct);

        if (identity == null)
        {
            throw new NotFoundException(nameof(AdvertiserIdentity), request.PageId);
        }

        var now = DateTimeOffset.UtcNow;

        if (string.Equals(request.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            var blockedReason = request.Error ?? "Meta Ad Library rate-limited or blocked direct scraper.";
            identity.Block(now.AddHours(24), blockedReason);
            unitOfWork.AdvertiserIdentities.Update(identity);
            await RecordBatchAsync(identity, 0, 0, 0, 0, 0, "Blocked", blockedReason, now, ct);
            logger.LogWarning("Marked Meta advertiser identity '{Name}' ({PageId}) as Blocked for 24h: {Error}", identity.Name, identity.ExternalId, request.Error);
            return new AdsIngestResponse("Blocked", Message: "Advertiser identity marked Blocked for 24h.");
        }

        if (string.Equals(request.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Meta collection failed for '{Name}' ({PageId}): {Error}. Not treating as removal.", identity.Name, identity.ExternalId, request.Error);
            return new AdsIngestResponse("Failed", Message: request.Error);
        }

        if (identity.BlockedUntil.HasValue)
        {
            identity.Unblock();
            unitOfWork.AdvertiserIdentities.Update(identity);
        }

        var existingAds = await readContext.Query<Ad>()
            .Where(a => a.IdentityId == identity.Id)
            .ToListAsync(ct);
        var existingByExternalId = existingAds.ToDictionary(a => a.ExternalAdId, StringComparer.OrdinalIgnoreCase);

        var competitorProducts = await readContext.Query<Product>()
            .Where(p => p.CompetitorId == identity.CompetitorId)
            .ToListAsync(ct);

        // Ensure handles are stored on products if missing
        var updatedHandles = false;
        foreach (var p in competitorProducts)
        {
            if (string.IsNullOrWhiteSpace(p.Handle) && !string.IsNullOrWhiteSpace(p.CanonicalUrl))
            {
                p.Handle = CreativeHashHelper.ExtractProductHandle(p.CanonicalUrl);
                if (!string.IsNullOrWhiteSpace(p.Handle))
                {
                    unitOfWork.Products.Update(p);
                    updatedHandles = true;
                }
            }
        }
        if (updatedHandles)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        var catalog = competitorProducts
            .Select(p => new LinkableProduct(p.Id, p.Title, p.Handle, p.CanonicalUrl))
            .ToList();

        var existingCampaigns = await readContext.Query<AdCampaign>()
            .Where(c => c.IdentityId == identity.Id)
            .ToListAsync(ct);
        var campaignsByHash = new Dictionary<string, AdCampaign>(StringComparer.OrdinalIgnoreCase);
        foreach (var existingCampaign in existingCampaigns)
        {
            campaignsByHash.TryAdd(existingCampaign.CreativeHash, existingCampaign);
        }
        var touchedCampaigns = new HashSet<AdCampaign>();

        var seenAdIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ingestedCount = 0;
        var updatedCount = 0;
        var duplicateCount = 0;
        var received = request.Ads?.Count ?? 0;

        if (request.Ads != null && request.Ads.Count > 0)
        {
            foreach (var rawAd in request.Ads)
            {
                if (!seenAdIds.Add(rawAd.Id))
                {
                    duplicateCount++;
                    continue;
                }

                // Campaign grouping: identity + normalized body
                var creativeHash = CreativeHashHelper.Compute(identity.Id, rawAd.Body);

                if (!campaignsByHash.TryGetValue(creativeHash, out var campaign))
                {
                    campaign = new AdCampaign
                    {
                        CompetitorId = identity.CompetitorId,
                        IdentityId = identity.Id,
                        CreativeHash = creativeHash,
                        Body = rawAd.Body,
                        Platform = AdPlatform.Meta,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        Status = AdStatus.Active
                    };
                    await unitOfWork.AdCampaigns.AddAsync(campaign, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    campaignsByHash[creativeHash] = campaign;
                }
                else
                {
                    campaign.LastSeenAt = now;
                    if (string.IsNullOrWhiteSpace(campaign.Body) && !string.IsNullOrWhiteSpace(rawAd.Body))
                    {
                        campaign.Body = rawAd.Body;
                    }
                    touchedCampaigns.Add(campaign);
                }

                var targetStatus = string.Equals(rawAd.Status, "Inactive", StringComparison.OrdinalIgnoreCase)
                    ? AdStatus.Inactive
                    : string.Equals(rawAd.Status, "Active", StringComparison.OrdinalIgnoreCase)
                        ? AdStatus.Active
                        : (rawAd.StopDate.HasValue && rawAd.StopDate.Value <= now ? AdStatus.Inactive : AdStatus.Active);

                if (existingByExternalId.TryGetValue(rawAd.Id, out var existing))
                {
                    existing.MissingCount = 0;
                    existing.LastSeenAt = now;
                    existing.CampaignId = campaign.Id;
                    existing.CreativeHash = creativeHash;
                    existing.Body = rawAd.Body ?? existing.Body;
                    existing.Title = rawAd.Title ?? existing.Title;
                    if (rawAd.StopDate.HasValue) existing.DeliveryStopDate = rawAd.StopDate.Value.ToUniversalTime();
                    if (!string.IsNullOrWhiteSpace(rawAd.Link)) existing.LinkUrl = rawAd.Link;
                    if (!string.IsNullOrWhiteSpace(rawAd.SnapshotUrl)) existing.SnapshotUrl = rawAd.SnapshotUrl;
                    if (!string.IsNullOrWhiteSpace(rawAd.ImageUrl)) existing.PreviewUrl = rawAd.ImageUrl;
                    AdDerivedFields.Apply(existing, catalog);

                    if (existing.Status == AdStatus.Active && targetStatus == AdStatus.Inactive)
                    {
                        existing.Status = AdStatus.Inactive;
                        var stopEvent = new ChangeEvent
                        {
                            RunId = Guid.Empty,
                            AdId = existing.Id,
                            Type = ChangeEventType.AdStopped,
                            OldValue = existing.Title ?? existing.ExternalAdId
                        };
                        await unitOfWork.ChangeEvents.AddAsync(stopEvent, ct);
                    }
                    else if (existing.Status == AdStatus.Inactive && targetStatus == AdStatus.Active)
                    {
                        existing.Status = AdStatus.Active;
                        var startEvent = new ChangeEvent
                        {
                            RunId = Guid.Empty,
                            AdId = existing.Id,
                            Type = ChangeEventType.AdStarted,
                            NewValue = existing.Title ?? existing.ExternalAdId
                        };
                        await unitOfWork.ChangeEvents.AddAsync(startEvent, ct);
                    }
                    unitOfWork.Ads.Update(existing);
                    updatedCount++;
                }
                else
                {
                    var newAd = new Ad
                    {
                        CompetitorId = identity.CompetitorId,
                        IdentityId = identity.Id,
                        CampaignId = campaign.Id,
                        ExternalAdId = rawAd.Id,
                        Platform = AdPlatform.Meta,
                        Title = rawAd.Title ?? identity.Name,
                        Body = rawAd.Body,
                        LinkUrl = rawAd.Link,
                        SnapshotUrl = rawAd.SnapshotUrl ?? $"https://www.facebook.com/ads/library/?id={rawAd.Id}",
                        PreviewUrl = rawAd.ImageUrl,
                        DeliveryStartDate = rawAd.StartDate?.ToUniversalTime(),
                        DeliveryStopDate = rawAd.StopDate?.ToUniversalTime(),
                        FirstSeenAt = (rawAd.StartDate ?? now).ToUniversalTime(),
                        LastSeenAt = now,
                        Status = targetStatus,
                        CreativeHash = creativeHash,
                        MissingCount = 0,
                        RawDataJson = rawAd.RawJson
                    };
                    AdDerivedFields.Apply(newAd, catalog);
                    await unitOfWork.Ads.AddAsync(newAd, ct);

                    if (targetStatus == AdStatus.Active)
                    {
                        var startEvent = new ChangeEvent
                        {
                            RunId = Guid.Empty,
                            AdId = newAd.Id,
                            Type = ChangeEventType.AdStarted,
                            NewValue = newAd.Title ?? newAd.ExternalAdId
                        };
                        await unitOfWork.ChangeEvents.AddAsync(startEvent, ct);
                    }
                    ingestedCount++;
                }
            }
        }

        // Handle missing ads from this Success run (ended when missing from 2 consecutive Success runs)
        var missingMarked = 0;
        foreach (var ad in existingAds)
        {
            if (!seenAdIds.Contains(ad.ExternalAdId) && ad.Status == AdStatus.Active)
            {
                ad.MissingCount++;
                if (ad.MissingCount >= 2)
                {
                    ad.Status = AdStatus.Inactive;
                    ad.DeliveryStopDate = now;
                    missingMarked++;
                    var stopEvent = new ChangeEvent
                    {
                        RunId = Guid.Empty,
                        AdId = ad.Id,
                        Type = ChangeEventType.AdStopped,
                        OldValue = ad.Title ?? ad.ExternalAdId
                    };
                    await unitOfWork.ChangeEvents.AddAsync(stopEvent, ct);
                }
                unitOfWork.Ads.Update(ad);
            }
        }

        foreach (var tc in touchedCampaigns)
        {
            unitOfWork.AdCampaigns.Update(tc);
        }

        await unitOfWork.AdIngestBatches.AddAsync(new AdIngestBatch
        {
            CompetitorId = identity.CompetitorId,
            IdentityId = identity.Id,
            Platform = AdPlatform.Meta,
            Received = received,
            Created = ingestedCount,
            Updated = updatedCount,
            Duplicate = duplicateCount,
            Skipped = 0,
            Status = "Success",
            ReceivedAt = now,
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Meta ads from '{Name}' ({PageId}): received {Received}, created {Created}, updated {Updated}, duplicate {Duplicate}, marked stopped {Stopped}.",
            identity.Name, identity.ExternalId, received, ingestedCount, updatedCount, duplicateCount, missingMarked);

        return new AdsIngestResponse("Success", IngestedCount: ingestedCount, TotalProcessed: seenAdIds.Count);
    }

    // One Google advertiser has one campaign: the first one stored for it, or a new one.
    private async Task<AdCampaign> GetOrCreateGoogleCampaignAsync(AdvertiserIdentity identity, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await readContext.Query<AdCampaign>()
            .Where(c => c.IdentityId == identity.Id && c.Platform == AdPlatform.Google)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return existing;
        }

        var campaign = new AdCampaign
        {
            CompetitorId = identity.CompetitorId,
            IdentityId = identity.Id,
            CreativeHash = CreativeHashHelper.Compute(identity.Id, null),
            Platform = AdPlatform.Google,
            FirstSeenAt = now,
            LastSeenAt = now,
            Status = AdStatus.Active
        };
        await unitOfWork.AdCampaigns.AddAsync(campaign, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return campaign;
    }

    private async Task<IReadOnlyList<LinkableProduct>> LoadCatalogAsync(Guid competitorId, CancellationToken ct) =>
        await readContext.Query<Product>()
            .Where(p => p.CompetitorId == competitorId)
            .Select(p => new LinkableProduct(p.Id, p.Title, p.Handle, p.CanonicalUrl))
            .ToListAsync(ct);

    private async Task RecordBatchAsync(
        AdvertiserIdentity identity,
        int received,
        int created,
        int updated,
        int duplicate,
        int skipped,
        string status,
        string? error,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await unitOfWork.AdIngestBatches.AddAsync(new AdIngestBatch
        {
            CompetitorId = identity.CompetitorId,
            IdentityId = identity.Id,
            Platform = identity.Platform,
            Received = received,
            Created = created,
            Updated = updated,
            Duplicate = duplicate,
            Skipped = skipped,
            Status = status,
            Error = error is { Length: > 500 } ? error[..500] : error,
            ReceivedAt = now,
        }, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // Short, safe reason for a creative the database rejected. The raw database message is only logged.
    private static string DescribeRejection(DbUpdateException ex)
    {
        var postgres = ex.InnerException as PostgresException;
        var column = string.IsNullOrEmpty(postgres?.ColumnName) ? string.Empty : $" ({postgres.ColumnName})";

        return postgres?.SqlState switch
        {
            "22001" => $"A field was longer than the database allows{column}.",
            "23505" => "This creative is already stored for this advertiser.",
            "23502" => $"A required field was empty{column}.",
            var state when state is not null && state.StartsWith("22", StringComparison.Ordinal) => $"The database rejected a value{column}.",
            _ => "The database rejected this creative."
        };
    }
}
