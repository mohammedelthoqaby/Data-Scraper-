using System.Security.Cryptography;
using System.Text;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Api.Controllers;

public record GoogleAdPayloadItem(
    string ExternalAdId,
    string? Format,
    string? Title,
    string? Body,
    string? SnapshotUrl,
    string? LinkUrl,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    string? RawJson);

public record GoogleAdsIngestRequest(
    string AdvertiserId,
    string? Status,
    string? Region,
    string? Error,
    IReadOnlyList<GoogleAdPayloadItem>? Ads);

public record GoogleIdentityDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    string ExternalId,
    string Name,
    string Status,
    DateTimeOffset? BlockedUntil);

[ApiController]
[Route("api/internal")]
[AllowAnonymous]
[EnableRateLimiting("internal")]
public class InternalApiController(
    ISender sender,
    IReadDbContext readContext,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<InternalApiController> logger) : ControllerBase
{
    private bool IsAuthorized()
    {
        var expectedToken = configuration["Internal:Token"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Internal-Token", out var val) || string.IsNullOrWhiteSpace(val))
        {
            return false;
        }

        var providedToken = val.ToString();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    [HttpPost("runs")]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TriggerRun(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var result = await sender.Send(new StartRunCommand(RunTrigger.Scheduled), ct);
        if (result.AlreadyActive)
        {
            return Ok(result);
        }

        return Accepted(result);
    }

    [HttpGet("ads/google/identities")]
    [ProducesResponseType(typeof(IReadOnlyList<GoogleIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetGoogleIdentities(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var identities = await (
            from i in readContext.Query<AdvertiserIdentity>()
            join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
            where i.Platform == AdPlatform.Google
            select new GoogleIdentityDto(
                i.Id,
                i.CompetitorId,
                c.Key,
                c.Name,
                i.ExternalId,
                i.Name,
                i.Status.ToString(),
                i.BlockedUntil)
        ).ToListAsync(ct);

        return Ok(identities);
    }

    [HttpPost("ads/google")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestGoogleAds([FromBody] GoogleAdsIngestRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (string.IsNullOrWhiteSpace(request.AdvertiserId))
        {
            return BadRequest(new { error = "AdvertiserId is required." });
        }

        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Google && i.ExternalId == request.AdvertiserId, ct);

        if (identity == null)
        {
            return NotFound(new { error = $"Google advertiser identity with external ID '{request.AdvertiserId}' not found." });
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
            return Ok(new { status = "Blocked", message = "Advertiser identity marked Blocked for 24h." });
        }

        var ingestedCount = 0;
        if (request.Ads != null && request.Ads.Count > 0)
        {
            foreach (var rawAd in request.Ads)
            {
                var creativeHash = CreativeHashHelper.Compute(rawAd.Title, rawAd.Body, rawAd.SnapshotUrl);

                var campaign = await readContext.Query<AdCampaign>()
                    .FirstOrDefaultAsync(c => c.IdentityId == identity.Id && c.CreativeHash == creativeHash, ct);

                if (campaign == null)
                {
                    campaign = new AdCampaign
                    {
                        CompetitorId = identity.CompetitorId,
                        IdentityId = identity.Id,
                        CreativeHash = creativeHash,
                        Platform = AdPlatform.Google,
                        FirstSeenAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow,
                        Status = AdStatus.Active
                    };
                    await unitOfWork.AdCampaigns.AddAsync(campaign, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                else
                {
                    campaign.LastSeenAt = DateTimeOffset.UtcNow;
                    unitOfWork.AdCampaigns.Update(campaign);
                }

                var existingAd = await readContext.Query<Ad>()
                    .FirstOrDefaultAsync(a => a.Platform == AdPlatform.Google && a.ExternalAdId == rawAd.ExternalAdId, ct);

                if (existingAd == null)
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
                        FirstSeenAt = (rawAd.FirstSeen ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                        LastSeenAt = (rawAd.LastSeen ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                        Status = AdStatus.Active,
                        CreativeHash = creativeHash,
                        RawDataJson = rawAd.RawJson
                    };
                    await unitOfWork.Ads.AddAsync(newAd, ct);
                    ingestedCount++;
                }
                else
                {
                    existingAd.LastSeenAt = (rawAd.LastSeen ?? DateTimeOffset.UtcNow).ToUniversalTime();
                    existingAd.DeliveryStopDate = rawAd.LastSeen?.ToUniversalTime();
                    if (existingAd.Status == AdStatus.Blocked)
                    {
                        existingAd.Status = AdStatus.Active;
                    }
                    unitOfWork.Ads.Update(existingAd);
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
        }

        return Ok(new { status = "Success", ingestedCount });
    }
}
