using LazyBean.Application.Ads.Commands.SyncAds;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Api.Controllers;

public record CreateManualAdRequest(
    Guid CompetitorId,
    string Platform,
    string? ExternalAdId,
    string Title,
    string? Body,
    string? LinkUrl,
    string? SnapshotUrl,
    DateTimeOffset? DeliveryStartDate,
    DateTimeOffset? DeliveryStopDate,
    string? Status);

[ApiController]
[Route("api/ads")]
[EnableRateLimiting("api")]
public class AdsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(GetAdsResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetAdsResponseDto>> GetAds(
        [FromQuery] Guid? competitorId,
        [FromQuery] Guid? identityId,
        [FromQuery] string? platform,
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetAdsQuery(competitorId, identityId, platform, status, from, to), ct);
        return Ok(result);
    }

    [HttpGet("campaigns")]
    [ProducesResponseType(typeof(IReadOnlyList<AdCampaignDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdCampaignDto>>> GetCampaigns(
        [FromQuery] Guid? competitorId,
        [FromQuery] string? platform,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var result = await sender.Send(new LazyBean.Application.Ads.Queries.GetCampaigns.GetCampaignsQuery(competitorId, platform, status), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdDto>> CreateManualAd(
        [FromBody] CreateManualAdRequest request,
        [FromServices] IReadDbContext readContext,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (request.CompetitorId == Guid.Empty)
        {
            return BadRequest(new { error = "CompetitorId is required." });
        }

        var competitor = await readContext.Query<Competitor>()
            .FirstOrDefaultAsync(c => c.Id == request.CompetitorId, ct);
        if (competitor == null)
        {
            return NotFound(new { error = $"Competitor with ID '{request.CompetitorId}' not found." });
        }

        if (!Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            return BadRequest(new { error = $"Invalid platform '{request.Platform}'. Must be Meta or Google." });
        }

        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.CompetitorId == competitor.Id && i.Platform == platform, ct);

        if (identity == null)
        {
            // Auto-create identity for this competitor/platform
            identity = new AdvertiserIdentity
            {
                CompetitorId = competitor.Id,
                Platform = platform,
                ExternalId = $"manual_{competitor.Key}",
                Name = competitor.Name,
                Status = AdvertiserIdentityStatus.Approved
            };
            await unitOfWork.AdvertiserIdentities.AddAsync(identity, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        var externalAdId = !string.IsNullOrWhiteSpace(request.ExternalAdId)
            ? request.ExternalAdId.Trim()
            : $"MANUAL_{Guid.NewGuid():N}";

        var creativeHash = CreativeHashHelper.Compute(identity.Id, request.Body);

        var campaign = await readContext.Query<AdCampaign>()
            .FirstOrDefaultAsync(c => c.IdentityId == identity.Id && c.CreativeHash == creativeHash, ct);

        var now = DateTimeOffset.UtcNow;
        if (campaign == null)
        {
            campaign = new AdCampaign
            {
                CompetitorId = competitor.Id,
                IdentityId = identity.Id,
                CreativeHash = creativeHash,
                Body = request.Body,
                Platform = platform,
                FirstSeenAt = now,
                LastSeenAt = now,
                Status = AdStatus.Active
            };
            await unitOfWork.AdCampaigns.AddAsync(campaign, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            campaign.LastSeenAt = now;
            if (string.IsNullOrWhiteSpace(campaign.Body) && !string.IsNullOrWhiteSpace(request.Body))
            {
                campaign.Body = request.Body;
            }
            unitOfWork.AdCampaigns.Update(campaign);
        }

        var targetStatus = Enum.TryParse<AdStatus>(request.Status, true, out var s) ? s : AdStatus.Active;

        var existingAd = await readContext.Query<Ad>()
            .FirstOrDefaultAsync(a => a.Platform == platform && a.ExternalAdId == externalAdId, ct);

        if (existingAd != null)
        {
            existingAd.Title = request.Title;
            existingAd.Body = request.Body;
            existingAd.LinkUrl = request.LinkUrl;
            existingAd.SnapshotUrl = request.SnapshotUrl;
            existingAd.Status = targetStatus;
            existingAd.LastSeenAt = now;
            unitOfWork.Ads.Update(existingAd);
            await unitOfWork.SaveChangesAsync(ct);

            return Ok(new AdDto(
                existingAd.Id,
                competitor.Id,
                competitor.Key,
                competitor.Name,
                identity.Id,
                identity.Name,
                existingAd.CampaignId,
                existingAd.ExternalAdId,
                existingAd.Platform.ToString(),
                existingAd.Title,
                existingAd.Body,
                existingAd.LinkUrl,
                existingAd.SnapshotUrl,
                existingAd.DeliveryStartDate,
                existingAd.DeliveryStopDate,
                existingAd.FirstSeenAt,
                existingAd.LastSeenAt,
                existingAd.Status.ToString(),
                existingAd.CreativeHash));
        }

        var ad = new Ad
        {
            CompetitorId = competitor.Id,
            IdentityId = identity.Id,
            CampaignId = campaign.Id,
            ExternalAdId = externalAdId,
            Platform = platform,
            Title = request.Title,
            Body = request.Body,
            LinkUrl = request.LinkUrl,
            SnapshotUrl = request.SnapshotUrl,
            DeliveryStartDate = request.DeliveryStartDate?.ToUniversalTime(),
            DeliveryStopDate = request.DeliveryStopDate?.ToUniversalTime(),
            FirstSeenAt = (request.DeliveryStartDate ?? now).ToUniversalTime(),
            LastSeenAt = now,
            Status = targetStatus,
            CreativeHash = creativeHash,
            MissingCount = 0
        };

        await unitOfWork.Ads.AddAsync(ad, ct);

        if (targetStatus == AdStatus.Active)
        {
            var startEvent = new ChangeEvent
            {
                RunId = Guid.Empty,
                AdId = ad.Id,
                Type = ChangeEventType.AdStarted,
                NewValue = ad.Title
            };
            await unitOfWork.ChangeEvents.AddAsync(startEvent, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        var dto = new AdDto(
            ad.Id,
            competitor.Id,
            competitor.Key,
            competitor.Name,
            identity.Id,
            identity.Name,
            ad.CampaignId,
            ad.ExternalAdId,
            ad.Platform.ToString(),
            ad.Title,
            ad.Body,
            ad.LinkUrl,
            ad.SnapshotUrl,
            ad.DeliveryStartDate,
            ad.DeliveryStopDate,
            ad.FirstSeenAt,
            ad.LastSeenAt,
            ad.Status.ToString(),
            ad.CreativeHash);

        return CreatedAtAction(nameof(GetAds), new { competitorId = competitor.Id }, dto);
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncAdsResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncAdsResultDto>> SyncAds(
        [FromQuery] Guid? competitorId,
        [FromQuery] string? platform,
        CancellationToken ct)
    {
        var result = await sender.Send(new SyncAdsCommand(competitorId, platform), ct);
        return Ok(result);
    }
}
