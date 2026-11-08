using LazyBean.Application.Ads.Common;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Ads.Commands.SyncAds;

public class SyncAdsCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    IMetaAdLibraryClient metaClient,
    IGoogleAdsSyncService googleAdsSyncService,
    ILogger<SyncAdsCommandHandler> logger)
    : IRequestHandler<SyncAdsCommand, SyncAdsResultDto>
{
    public async Task<SyncAdsResultDto> Handle(SyncAdsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        var totalFetched = 0;
        var newAdsCount = 0;
        var activeAdsCount = 0;
        var campaignsCreated = 0;
        var identitiesProcessed = 0;

        // If syncing Google specifically (or all platforms), run the in-app Google Ads sync service
        if (string.IsNullOrWhiteSpace(request.Platform) ||
            string.Equals(request.Platform, "google", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var gResult = await googleAdsSyncService.SyncGoogleAdsAsync(force: true, ct);
                identitiesProcessed += gResult.AdvertisersProcessed;
                totalFetched += gResult.AdsIngested;
                newAdsCount += gResult.AdsIngested;
                campaignsCreated += gResult.CampaignsUpdated;
                if (gResult.Errors.Count > 0)
                {
                    errors.AddRange(gResult.Errors);
                }
            }
            catch (Exception ex)
            {
                var errMsg = $"Failed to execute Google Ads sync: {ex.Message}";
                logger.LogError(ex, "{Error}", errMsg);
                errors.Add(errMsg);
            }
        }

        var identitiesQuery = readContext.Query<AdvertiserIdentity>()
            .Where(i => i.Status == AdvertiserIdentityStatus.Approved);

        if (request.CompetitorId.HasValue)
        {
            identitiesQuery = identitiesQuery.Where(i => i.CompetitorId == request.CompetitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Platform) &&
            Enum.TryParse<AdPlatform>(request.Platform, true, out var filterPlatform))
        {
            identitiesQuery = identitiesQuery.Where(i => i.Platform == filterPlatform);
        }

        var identities = await identitiesQuery.ToListAsync(ct);

        foreach (var identity in identities)
        {
            try
            {
                if (identity.Platform == AdPlatform.Meta)
                {
                    identitiesProcessed++;
                    var tokenDebug = await metaClient.DebugTokenAsync(ct);
                    if (!tokenDebug.IsValid)
                    {
                        var err = $"Meta sync failed for identity '{identity.Name}': {tokenDebug.ErrorMessage}";
                        logger.LogError("{Error}", err);
                        errors.Add(err);
                        continue;
                    }

                    var metaAds = await metaClient.FetchAdsAsync(identity.ExternalId, ct);
                    totalFetched += metaAds.Count;

                    foreach (var rawAd in metaAds)
                    {
                        var creativeHash = CreativeHashHelper.Compute(rawAd.Title, rawAd.Body, rawAd.SnapshotUrl);

                        // Find or create campaign
                        var campaign = await readContext.Query<AdCampaign>()
                            .FirstOrDefaultAsync(c => c.IdentityId == identity.Id && c.CreativeHash == creativeHash, ct);

                        if (campaign == null)
                        {
                            campaign = new AdCampaign
                            {
                                CompetitorId = identity.CompetitorId,
                                IdentityId = identity.Id,
                                CreativeHash = creativeHash,
                                Platform = AdPlatform.Meta,
                                FirstSeenAt = DateTimeOffset.UtcNow,
                                LastSeenAt = DateTimeOffset.UtcNow,
                                Status = AdStatus.Active
                            };
                            await unitOfWork.AdCampaigns.AddAsync(campaign, ct);
                            campaignsCreated++;
                        }
                        else
                        {
                            campaign.LastSeenAt = DateTimeOffset.UtcNow;
                            unitOfWork.AdCampaigns.Update(campaign);
                        }

                        // Determine status
                        var isInactive = rawAd.DeliveryStopTime.HasValue && rawAd.DeliveryStopTime.Value <= DateTimeOffset.UtcNow;
                        var adStatus = isInactive ? AdStatus.Inactive : AdStatus.Active;

                        var existingAd = await readContext.Query<Ad>()
                            .FirstOrDefaultAsync(a => a.Platform == AdPlatform.Meta && a.ExternalAdId == rawAd.Id, ct);

                        if (existingAd == null)
                        {
                            var newAd = new Ad
                            {
                                CompetitorId = identity.CompetitorId,
                                IdentityId = identity.Id,
                                CampaignId = campaign.Id,
                                ExternalAdId = rawAd.Id,
                                Platform = AdPlatform.Meta,
                                Title = rawAd.Title,
                                Body = rawAd.Body,
                                LinkUrl = rawAd.LinkUrl,
                                SnapshotUrl = rawAd.SnapshotUrl,
                                DeliveryStartDate = rawAd.DeliveryStartTime?.ToUniversalTime(),
                                DeliveryStopDate = rawAd.DeliveryStopTime?.ToUniversalTime(),
                                FirstSeenAt = (rawAd.DeliveryStartTime ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                                LastSeenAt = DateTimeOffset.UtcNow,
                                Status = adStatus,
                                CreativeHash = creativeHash,
                                RawDataJson = rawAd.RawJson,
                                Placements = AdPlacements.Normalize(rawAd.Placements)
                            };

                            await unitOfWork.Ads.AddAsync(newAd, ct);
                            newAdsCount++;
                            if (adStatus == AdStatus.Active) activeAdsCount++;

                            // Emit AdStarted change event
                            var startEvent = new ChangeEvent
                            {
                                RunId = Guid.Empty,
                                AdId = newAd.Id,
                                Type = ChangeEventType.AdStarted,
                                NewValue = newAd.Title ?? newAd.ExternalAdId
                            };
                        }
                        else
                        {
                            existingAd.LastSeenAt = DateTimeOffset.UtcNow;
                            existingAd.DeliveryStopDate = rawAd.DeliveryStopTime?.ToUniversalTime();
                            existingAd.Placements = AdPlacements.Normalize(rawAd.Placements) ?? existingAd.Placements;

                            if (existingAd.Status == AdStatus.Active && adStatus == AdStatus.Inactive)
                            {
                                existingAd.Status = AdStatus.Inactive;
                                var stopEvent = new ChangeEvent
                                {
                                    RunId = Guid.Empty,
                                    AdId = existingAd.Id,
                                    Type = ChangeEventType.AdStopped,
                                    OldValue = existingAd.Title ?? existingAd.ExternalAdId
                                };
                            }
                            else if (adStatus == AdStatus.Active)
                            {
                                activeAdsCount++;
                            }

                            unitOfWork.Ads.Update(existingAd);
                        }
                    }

                    await unitOfWork.SaveChangesAsync(ct);
                }
                else if (identity.Platform == AdPlatform.Google)
                {
                    // Google Ads are collected in-app via IGoogleAdsSyncService
                    continue;
                }


            }
            catch (Exception ex)
            {
                var errMsg = $"Error syncing ads for identity '{identity.Name}': {ex.Message}";
                logger.LogError(ex, "{Error}", errMsg);
                errors.Add(errMsg);
            }
        }

        return new SyncAdsResultDto(
            identitiesProcessed,
            totalFetched,
            newAdsCount,
            activeAdsCount,
            campaignsCreated,
            errors);
    }
}
