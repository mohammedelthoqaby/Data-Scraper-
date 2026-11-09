using LazyBean.Application.Ads.Collection;
using LazyBean.Domain.Rules;
using LazyBean.Infrastructure.External.Google;
using LazyBean.Infrastructure.External.Http;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Infrastructure.External.Woo;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

public class GoogleAdsSyncService(
    IGoogleAdsTransparencyClient transparencyClient,
    IReadDbContext readContext,
    IUnitOfWork unitOfWork,
    ILogger<GoogleAdsSyncService> logger)
    : IGoogleAdsSyncService
{
    private static readonly TimeSpan MinimumSyncInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan BlockedBackoffDuration = TimeSpan.FromHours(24);

    public bool EnableRateLimitingDelays { get; set; } = true;

    public async Task<GoogleAdsSyncResult?> SyncGoogleAdsIfDueAsync(CancellationToken ct = default)
    {
        var latestGoogleAd = await readContext.Query<Ad>()
            .Where(a => a.Platform == AdPlatform.Google)
            .OrderByDescending(a => a.LastSeenAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        if (latestGoogleAd != null && latestGoogleAd.LastSeenAt > now - MinimumSyncInterval)
        {
            var nextDue = latestGoogleAd.LastSeenAt + MinimumSyncInterval;
            logger.LogInformation(
                "Skipping scheduled Google Ads sync: last synced at {LastSeen}, next due after {NextDue}.",
                latestGoogleAd.LastSeenAt,
                nextDue);
            return new GoogleAdsSyncResult(false, $"Synced recently at {latestGoogleAd.LastSeenAt}. Next due after {nextDue}.", 0, 0, 0, [], []);
        }

        logger.LogInformation("7+ days elapsed since last Google Ads sync. Starting weekly Google Ads collection pass...");
        return await SyncGoogleAdsAsync(force: false, ct);
    }

    public async Task<GoogleAdsSyncResult> SyncGoogleAdsAsync(bool force, CancellationToken ct = default)
    {
        var identityIds = await (
            from i in readContext.Query<AdvertiserIdentity>()
            where i.Platform == AdPlatform.Google && i.Status == AdvertiserIdentityStatus.Approved
            select i.Id
        ).ToListAsync(ct);

        if (identityIds.Count == 0)
        {
            logger.LogInformation("No approved Google advertiser identities found. Exiting sync.");
            return new GoogleAdsSyncResult(true, "No approved Google advertiser identities.", 0, 0, 0, [], []);
        }

        logger.LogInformation("Found {Count} approved Google advertiser identities to sync.", identityIds.Count);

        var processedCount = 0;
        var totalIngested = 0;
        var campaignsUpdated = 0;
        var blockedList = new List<string>();
        var errors = new List<string>();

        var now = DateTimeOffset.UtcNow;

        foreach (var identityId in identityIds)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var identity = await unitOfWork.AdvertiserIdentities.GetByIdAsync(identityId, ct);
            if (identity == null)
            {
                continue;
            }

            if (!force && identity.BlockedUntil.HasValue && identity.BlockedUntil.Value > now)
            {
                logger.LogWarning(
                    "Skipping Google advertiser '{Name}' ({ExternalId}) - blocked until {BlockedUntil}.",
                    identity.Name,
                    identity.ExternalId,
                    identity.BlockedUntil.Value);
                blockedList.Add($"{identity.Name} (Blocked until {identity.BlockedUntil.Value:u})");
                continue;
            }

            logger.LogInformation("Processing Google advertiser '{Name}' ({ExternalId})...", identity.Name, identity.ExternalId);

            try
            {
                // 1. Resolve and cache advertiser ID if not yet resolved
                var advertiserId = identity.ExternalId;
                if (!advertiserId.StartsWith("AR", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Resolving Google advertiser ID for '{Name}' (current external ID: '{ExternalId}')...", identity.Name, identity.ExternalId);

                    await DelayPolitelyAsync(ct);
                    var resolvedId = await transparencyClient.SearchAdvertiserIdAsync(identity.Name, ct);

                    if (string.IsNullOrWhiteSpace(resolvedId) && !string.IsNullOrWhiteSpace(identity.ExternalId))
                    {
                        var domainQuery = identity.ExternalId.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                        resolvedId = await transparencyClient.SearchAdvertiserIdAsync(domainQuery, ct);
                    }

                    if (string.IsNullOrWhiteSpace(resolvedId))
                    {
                        logger.LogWarning("Could not resolve Google advertiser ID for '{Name}'. Skipping.", identity.Name);
                        errors.Add($"Could not resolve Google advertiser ID for '{identity.Name}'.");
                        continue;
                    }

                    // Cache and persist resolved advertiser ID
                    identity.ExternalId = resolvedId;
                    unitOfWork.AdvertiserIdentities.Update(identity);
                    await unitOfWork.SaveChangesAsync(ct);
                    advertiserId = resolvedId;

                    logger.LogInformation("Cached resolved Google advertiser ID '{AdvId}' for '{Name}'.", resolvedId, identity.Name);
                }

                // 2. Fetch creatives with pagination (3-5s between requests)
                await DelayPolitelyAsync(ct);

                // The same reading the desktop app does: four pages at most, 100 creatives at most.
                var collected = await GoogleCreativeCollector.CollectAsync(
                    transparencyClient, advertiserId, pauseCt => DelayPolitelyAsync(pauseCt), ct);
                var allCreatives = collected.Creatives.ToList();

                logger.LogInformation("Retrieved {Count} creatives across {Pages} page(s) for '{Name}'.", allCreatives.Count, collected.Pages, identity.Name);

                // 3. Ingest ads, campaigns, events, and product mapping
                var ingested = await IngestCreativesForIdentityAsync(identity, allCreatives, ct);
                totalIngested += ingested.NewAdsCount;
                campaignsUpdated += ingested.CampaignsUpdated;
                processedCount++;

                // Polite spacing before next advertiser
                await DelayPolitelyAsync(ct);
            }
            catch (GoogleAdsBlockedException ex)
            {
                logger.LogWarning(ex, "Google Ads Transparency blocked or rate-limited requests for '{Name}'. Backing off for 24h.", identity.Name);

                identity.Block(DateTimeOffset.UtcNow.Add(BlockedBackoffDuration), ex.Message);
                unitOfWork.AdvertiserIdentities.Update(identity);
                await unitOfWork.SaveChangesAsync(ct);

                blockedList.Add($"{identity.Name}: {ex.Message}");
                // Break on platform-wide rate limit/captcha to respect 24h backoff
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error collecting Google Ads for '{Name}'.", identity.Name);
                errors.Add($"Error on '{identity.Name}': {ex.Message}");
            }
        }

        return new GoogleAdsSyncResult(
            true,
            "Sync pass completed.",
            processedCount,
            totalIngested,
            campaignsUpdated,
            blockedList,
            errors);
    }

    private async Task<(int NewAdsCount, int CampaignsUpdated)> IngestCreativesForIdentityAsync(
        AdvertiserIdentity identity,
        IReadOnlyList<GoogleCreativeDto> rawCreatives,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

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

        // Ensure handles are extracted for matching
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
        var newlyAddedAds = new Dictionary<string, Ad>(StringComparer.OrdinalIgnoreCase);
        var newAdsCount = 0;

        foreach (var rawAd in rawCreatives)
        {
            seenAdIds.Add(rawAd.ExternalAdId);

            // Group campaigns by creative hash
            var creativeHash = CreativeHashHelper.Compute(identity.Id, rawAd.Body ?? rawAd.ImageUrl ?? rawAd.ExternalAdId);

            if (!campaignsByHash.TryGetValue(creativeHash, out var campaign))
            {
                campaign = new AdCampaign
                {
                    CompetitorId = identity.CompetitorId,
                    IdentityId = identity.Id,
                    CreativeHash = creativeHash,
                    Body = rawAd.Body ?? rawAd.Title,
                    Platform = AdPlatform.Google,
                    FirstSeenAt = rawAd.FirstSeen ?? now,
                    LastSeenAt = rawAd.LastSeen ?? now,
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
            if (!string.IsNullOrWhiteSpace(rawAd.LinkUrl))
            {
                var adHandle = CreativeHashHelper.ExtractProductHandle(rawAd.LinkUrl);
                if (!string.IsNullOrWhiteSpace(adHandle) && productsByHandle.TryGetValue(adHandle, out var foundByHandle))
                {
                    matchedProductId = foundByHandle.Id;
                }
                else
                {
                    var normUrl = CreativeHashHelper.NormalizeProductUrl(rawAd.LinkUrl);
                    if (productsByNormUrl.TryGetValue(normUrl, out var foundByUrl))
                    {
                        matchedProductId = foundByUrl.Id;
                    }
                }
            }

            var isExpired = rawAd.LastSeen.HasValue && AdActivity.IsExpired(rawAd.LastSeen.Value, AdActivity.CutoffFor(now, AdActivity.DefaultWindowDays));
            var targetStatus = isExpired ? AdStatus.Inactive : AdStatus.Active;

            if (newlyAddedAds.TryGetValue(rawAd.ExternalAdId, out var justAdded))
            {
                justAdded.LastSeenAt = (rawAd.LastSeen ?? now).ToUniversalTime();
                if (!string.IsNullOrWhiteSpace(rawAd.Body)) justAdded.Body = rawAd.Body;
                if (matchedProductId.HasValue) justAdded.ProductId = matchedProductId;
            }
            else if (existingByExternalId.TryGetValue(rawAd.ExternalAdId, out var existing))
            {
                existing.MissingCount = 0;
                existing.LastSeenAt = rawAd.LastSeen ?? now;
                existing.CampaignId = campaign.Id;
                existing.CreativeHash = creativeHash;
                if (!string.IsNullOrWhiteSpace(rawAd.Body)) existing.Body = rawAd.Body;
                if (rawAd.LastSeen.HasValue) existing.DeliveryStopDate = rawAd.LastSeen.Value.ToUniversalTime();
                if (matchedProductId.HasValue) existing.ProductId = matchedProductId;
                if (!string.IsNullOrWhiteSpace(rawAd.LinkUrl)) existing.LinkUrl = rawAd.LinkUrl;
                if (!string.IsNullOrWhiteSpace(rawAd.SnapshotUrl)) existing.SnapshotUrl = rawAd.SnapshotUrl;
                if (existing.Status == AdStatus.Blocked) existing.Status = AdStatus.Active;

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
                    ExternalAdId = rawAd.ExternalAdId,
                    Platform = AdPlatform.Google,
                    Title = rawAd.Title ?? identity.Name,
                    Body = rawAd.Body,
                    LinkUrl = rawAd.LinkUrl,
                    SnapshotUrl = rawAd.SnapshotUrl,
                    DeliveryStartDate = rawAd.FirstSeen?.ToUniversalTime(),
                    DeliveryStopDate = rawAd.LastSeen?.ToUniversalTime(),
                    FirstSeenAt = (rawAd.FirstSeen ?? now).ToUniversalTime(),
                    LastSeenAt = (rawAd.LastSeen ?? now).ToUniversalTime(),
                    Status = targetStatus,
                    CreativeHash = creativeHash,
                    MissingCount = 0,
                    ProductId = matchedProductId,
                    RawDataJson = rawAd.RawJson
                };

                await unitOfWork.Ads.AddAsync(newAd, ct);
                newlyAddedAds[newAd.ExternalAdId] = newAd;

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

                newAdsCount++;
            }
        }

        // Handle missing ads: increment missing count; if >= 2, mark Inactive + AdStopped
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
        return (newAdsCount, touchedCampaigns.Count);
    }

    private async Task DelayPolitelyAsync(CancellationToken ct)
    {
        if (!EnableRateLimitingDelays)
        {
            return;
        }

        var delayMs = Random.Shared.Next(3000, 5001);
        await Task.Delay(delayMs, ct);
    }
}
