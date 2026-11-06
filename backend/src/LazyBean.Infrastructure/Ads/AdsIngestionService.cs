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

// Ads ingestion shared by the internal API and the paired desktop collector. Matches each ad to a
// product, groups it into campaigns, records start/stop change events and marks ads missing after
// two consecutive successful collections.
public class AdsIngestionService(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
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

        if (string.Equals(request.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            identity.Block(DateTimeOffset.UtcNow.AddHours(24), request.Error ?? "Google Ads Transparency blocked/rate-limited direct scraper.");
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
                    FirstSeenAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow,
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
                    ega.LastSeenAt = DateTimeOffset.UtcNow;
                    unitOfWork.Ads.Update(ega);
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning("Marked Google advertiser identity '{Name}' as Blocked for 24h.", identity.Name);
            return new AdsIngestResponse("Blocked", Message: "Advertiser identity marked Blocked for 24h.");
        }

        var results = new List<AdsIngestItemResult>();
        var ingestedCount = 0;

        if (request.Ads is { Count: > 0 })
        {
            var now = DateTimeOffset.UtcNow;
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

            // Existing ads and campaigns are read once, so every creative in the batch sees the same tracked state.
            var acceptedIds = accepted.Select(a => a.ExternalAdId).ToList();
            var existingAds = acceptedIds.Count == 0
                ? new List<Ad>()
                : await readContext.Query<Ad>()
                    .Where(a => a.Platform == AdPlatform.Google && acceptedIds.Contains(a.ExternalAdId))
                    .ToListAsync(ct);

            var existingByExternalId = new Dictionary<string, Ad>(StringComparer.Ordinal);
            foreach (var existing in existingAds)
            {
                existingByExternalId.TryAdd(existing.ExternalAdId, existing);
            }

            var existingCampaigns = await readContext.Query<AdCampaign>()
                .Where(c => c.IdentityId == identity.Id)
                .ToListAsync(ct);

            var campaignsByHash = new Dictionary<string, AdCampaign>(StringComparer.Ordinal);
            foreach (var existing in existingCampaigns)
            {
                campaignsByHash.TryAdd(existing.CreativeHash, existing);
            }

            // Each creative is saved on its own, so one rejected creative cannot fail the whole batch.
            foreach (var ad in accepted)
            {
                try
                {
                    var creativeHash = CreativeHashHelper.Compute(ad.Title, ad.Body, ad.SnapshotUrl);

                    if (!campaignsByHash.TryGetValue(creativeHash, out var campaign))
                    {
                        campaign = new AdCampaign
                        {
                            CompetitorId = identity.CompetitorId,
                            IdentityId = identity.Id,
                            CreativeHash = creativeHash,
                            Platform = AdPlatform.Google,
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
                        unitOfWork.AdCampaigns.Update(campaign);
                    }

                    string outcome;
                    if (existingByExternalId.TryGetValue(ad.ExternalAdId, out var existingAd))
                    {
                        existingAd.LastSeenAt = ad.LastSeen ?? now;
                        existingAd.DeliveryStopDate = ad.LastSeen;
                        if (existingAd.Status == AdStatus.Blocked)
                        {
                            existingAd.Status = AdStatus.Active;
                        }

                        unitOfWork.Ads.Update(existingAd);
                        outcome = "updated";
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
                            DeliveryStartDate = ad.FirstSeen,
                            DeliveryStopDate = ad.LastSeen,
                            FirstSeenAt = ad.FirstSeen ?? now,
                            LastSeenAt = ad.LastSeen ?? now,
                            Status = AdStatus.Active,
                            CreativeHash = creativeHash,
                            RawDataJson = ad.RawJson
                        };
                        await unitOfWork.Ads.AddAsync(newAd, ct);
                        outcome = "created";
                    }

                    await unitOfWork.SaveChangesAsync(ct);
                    results.Add(new AdsIngestItemResult(ad.ExternalAdId, outcome));
                    if (outcome == "created")
                    {
                        ingestedCount++;
                    }
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
        }

        var skipped = results.Count(r => r.Outcome == "skipped");
        return new AdsIngestResponse(
            "Success",
            Message: skipped == 0 ? null : $"{skipped} creative(s) skipped; see items for the reason.",
            IngestedCount: ingestedCount,
            Items: results);
    }

    // Short, safe reason for a creative the database rejected. The raw database message is only logged.
    private static string DescribeRejection(DbUpdateException ex)
    {
        var postgres = ex.InnerException as PostgresException;
        var column = string.IsNullOrEmpty(postgres?.ColumnName) ? string.Empty : $" ({postgres.ColumnName})";

        return postgres?.SqlState switch
        {
            "22001" => $"A field was longer than the database allows{column}.",
            "23505" => "This creative is already stored under another advertiser.",
            "23502" => $"A required field was empty{column}.",
            var state when state is not null && state.StartsWith("22", StringComparison.Ordinal) => $"The database rejected a value{column}.",
            _ => "The database rejected this creative."
        };
    }

    public async Task<AdsIngestResponse> IngestMetaPageAsync(MetaAdsIngestRequest request, CancellationToken ct = default)
    {
        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Meta && i.ExternalId == request.PageId, ct);

        if (identity == null)
        {
            throw new NotFoundException(nameof(AdvertiserIdentity), request.PageId);
        }

        if (string.Equals(request.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            identity.Block(DateTimeOffset.UtcNow.AddHours(24), request.Error ?? "Meta Ad Library rate-limited or blocked direct scraper.");
            unitOfWork.AdvertiserIdentities.Update(identity);
            await unitOfWork.SaveChangesAsync(ct);
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

        // Fast lookups for ad -> product matching
        var productsByHandle = competitorProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .GroupBy(p => p.Handle!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var productsByNormUrl = competitorProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.CanonicalUrl))
            .GroupBy(p => CreativeHashHelper.NormalizeProductUrl(p.CanonicalUrl), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var existingCampaigns = await readContext.Query<AdCampaign>()
            .Where(c => c.IdentityId == identity.Id)
            .ToListAsync(ct);
        var campaignsByHash = existingCampaigns.ToDictionary(c => c.CreativeHash, StringComparer.OrdinalIgnoreCase);
        var touchedCampaigns = new HashSet<AdCampaign>();

        var seenAdIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var ingestedCount = 0;

        if (request.Ads != null && request.Ads.Count > 0)
        {
            foreach (var rawAd in request.Ads)
            {
                seenAdIds.Add(rawAd.Id);
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

                Guid? matchedProductId = null;
                if (!string.IsNullOrWhiteSpace(rawAd.Link))
                {
                    var adHandle = CreativeHashHelper.ExtractProductHandle(rawAd.Link);
                    if (!string.IsNullOrWhiteSpace(adHandle) && productsByHandle.TryGetValue(adHandle, out var foundByHandle))
                    {
                        matchedProductId = foundByHandle.Id;
                    }
                    else
                    {
                        var normAdUrl = CreativeHashHelper.NormalizeProductUrl(rawAd.Link);
                        if (productsByNormUrl.TryGetValue(normAdUrl, out var foundByUrl))
                        {
                            matchedProductId = foundByUrl.Id;
                        }
                    }
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
                    if (rawAd.StopDate.HasValue) existing.DeliveryStopDate = rawAd.StopDate.Value.ToUniversalTime();
                    if (matchedProductId.HasValue) existing.ProductId = matchedProductId;
                    if (!string.IsNullOrWhiteSpace(rawAd.Link)) existing.LinkUrl = rawAd.Link;
                    if (!string.IsNullOrWhiteSpace(rawAd.SnapshotUrl)) existing.SnapshotUrl = rawAd.SnapshotUrl;

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
                        DeliveryStartDate = rawAd.StartDate?.ToUniversalTime(),
                        DeliveryStopDate = rawAd.StopDate?.ToUniversalTime(),
                        FirstSeenAt = (rawAd.StartDate ?? now).ToUniversalTime(),
                        LastSeenAt = now,
                        Status = targetStatus,
                        CreativeHash = creativeHash,
                        MissingCount = 0,
                        ProductId = matchedProductId,
                        RawDataJson = rawAd.RawJson
                    };
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
        foreach (var ad in existingAds)
        {
            if (!seenAdIds.Contains(ad.ExternalAdId) && ad.Status == AdStatus.Active)
            {
                ad.MissingCount++;
                if (ad.MissingCount >= 2)
                {
                    ad.Status = AdStatus.Inactive;
                    ad.DeliveryStopDate = now;
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

        await unitOfWork.SaveChangesAsync(ct);
        return new AdsIngestResponse("Success", IngestedCount: ingestedCount, TotalProcessed: seenAdIds.Count);
    }
}
