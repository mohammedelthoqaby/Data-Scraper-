using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Application.Runs.Queries.GetRuns;
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

public record MetaIdentityDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    string PageId,
    string Name,
    string Status,
    DateTimeOffset? BlockedUntil);

public record MetaAdItemRequest(
    string Id,
    string? Title,
    string? Body,
    string? Link,
    string? SnapshotUrl,
    string? ImageUrl,
    DateTimeOffset? StartDate,
    DateTimeOffset? StopDate,
    string? Status,
    string? RawJson);

public record MetaAdsIngestRequest(
    string PageId,
    string Status,
    string? Error,
    List<MetaAdItemRequest>? Ads);


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

    [HttpGet("runs/latest")]
    [ProducesResponseType(typeof(RunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLatestRun(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var runs = await sender.Send(new GetRunsQuery(), ct);
        var latest = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (latest == null)
        {
            return NotFound(new { error = "No runs found." });
        }

        return Ok(latest);
    }

    [HttpPost("ads/google/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SyncGoogleAds(
        [FromQuery] bool force,
        [FromServices] IGoogleAdsSyncService googleAdsSyncService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var result = await googleAdsSyncService.SyncGoogleAdsAsync(force, ct);
        return Ok(result);
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

    [HttpGet("ads/meta/identities")]
    [ProducesResponseType(typeof(IReadOnlyList<MetaIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMetaIdentities(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var identities = await (
            from i in readContext.Query<AdvertiserIdentity>()
            join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
            where i.Platform == AdPlatform.Meta && i.Status == AdvertiserIdentityStatus.Approved
            select new MetaIdentityDto(
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

    [HttpPost("ads/meta")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestMetaAds([FromBody] MetaAdsIngestRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (string.IsNullOrWhiteSpace(request.PageId))
        {
            return BadRequest(new { error = "PageId is required." });
        }

        var identity = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Meta && i.ExternalId == request.PageId, ct);

        if (identity == null)
        {
            return NotFound(new { error = $"Meta advertiser identity with Page ID '{request.PageId}' not found." });
        }

        if (string.Equals(request.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            identity.Block(DateTimeOffset.UtcNow.AddHours(24), request.Error ?? "Meta Ad Library rate-limited or blocked direct scraper.");
            unitOfWork.AdvertiserIdentities.Update(identity);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning("Marked Meta advertiser identity '{Name}' ({PageId}) as Blocked for 24h: {Error}", identity.Name, identity.ExternalId, request.Error);
            return Ok(new { status = "Blocked", message = "Advertiser identity marked Blocked for 24h." });
        }

        if (string.Equals(request.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Meta collection failed for '{Name}' ({PageId}): {Error}. Not treating as removal.", identity.Name, identity.ExternalId, request.Error);
            return Ok(new { status = "Failed", message = request.Error });
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
        return Ok(new { status = "Success", ingestedCount, totalProcessed = seenAdIds.Count });
    }

    [HttpGet("diagnostics/strong-coffee")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DiagnoseStrongCoffee([FromServices] IHttpClientFactory httpClientFactory, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var url = "https://strongcoffee-eg.com/wp-json/wc/store/v1/products?per_page=100&page=1";

        async Task<object> ProbeAsync(string ua)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
                using var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                var snippet = body.Length > 200 ? body[..200] : body;
                resp.Headers.TryGetValues("CF-RAY", out var cfRay);
                return new
                {
                    userAgent = ua,
                    statusCode = (int)resp.StatusCode,
                    status = resp.StatusCode.ToString(),
                    server = resp.Headers.Server?.ToString() ?? "",
                    cfRay = cfRay != null ? string.Join(", ", cfRay) : null,
                    bodySnippet = snippet
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    userAgent = ua,
                    statusCode = 0,
                    error = ex.Message
                };
            }
        }

        var currentUa = await ProbeAsync("LazyBean-Collector/1.0");
        var honestUa = await ProbeAsync("LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)");

        return Ok(new
        {
            targetUrl = url,
            currentUa,
            honestUa
        });
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/products")]
    [ProducesResponseType(typeof(IngestBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestCompetitorProducts(
        Guid runId,
        string slug,
        [FromBody] IngestProductsBatchRequest request,
        [FromServices] ICompetitorProductIngestionService ingestionService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (request?.Products == null)
        {
            return BadRequest(new { error = "Products payload is required." });
        }

        var result = await ingestionService.IngestBatchAsync(
            runId,
            slug,
            request.Page,
            request.TotalPages,
            request.Products,
            ct);

        return Ok(result);
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/complete")]
    [ProducesResponseType(typeof(CompleteCompetitorResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteCompetitorCollection(
        Guid runId,
        string slug,
        [FromBody] CompleteCompetitorRunRequest request,
        [FromServices] ICompetitorProductIngestionService ingestionService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (request == null)
        {
            return BadRequest(new { error = "Complete payload is required." });
        }

        var result = await ingestionService.CompleteCompetitorAsync(
            runId,
            slug,
            request.Status,
            request.TotalPages,
            request.TotalProducts,
            request.Error,
            ct);

        return Ok(result);
    }

    private static string StripTrackingParams(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/');
        }
        catch
        {
            var qIdx = url.IndexOf('?');
            return (qIdx > 0 ? url[..qIdx] : url).TrimEnd('/');
        }
    }
}
