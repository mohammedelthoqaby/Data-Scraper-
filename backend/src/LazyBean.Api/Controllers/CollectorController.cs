using System.Security.Claims;
using LazyBean.Api.Auth;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Commands.PairCollectorDevice;
using LazyBean.Application.Collector.Commands.RevokeCollectorDevice;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Api.Controllers;

// Endpoints used by the paired LazyBean Collector desktop app. Everything except pairing needs a
// device token (see DeviceTokenAuthenticationHandler) with the matching scope; dashboard cookies are
// not accepted here.
[ApiController]
[Route("api/collector")]
public class CollectorController(
    ISender sender,
    IReadDbContext readContext,
    ICollectorJobService jobs,
    IAdsIngestionService adsIngestion,
    ICompetitorProductIngestionService productIngestion) : ControllerBase
{
    private Guid CurrentDeviceId => Guid.Parse(User.FindFirstValue(CollectorClaims.DeviceId)!);

    // Pairing signs in with dashboard credentials, so it is anonymous and tightly rate-limited.
    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting("collector-pair")]
    [ProducesResponseType(typeof(PairCollectorDeviceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PairCollectorDeviceResultDto>> Pair(
        [FromBody] PairCollectorDeviceCommand command,
        CancellationToken ct)
    {
        return Ok(await sender.Send(command, ct));
    }

    // Polled every 10 seconds. Returns the device's current claim, a newly claimed job, or 204.
    [HttpGet("jobs/next")]
    [Authorize(Policy = CollectorPolicies.AnyDevice)]
    [ProducesResponseType(typeof(CollectorJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> NextJob(CancellationToken ct)
    {
        var job = await jobs.ClaimNextAsync(CurrentDeviceId, ct);
        return job is null ? NoContent() : Ok(job);
    }

    // Sent while the app works on a job, so a long collection keeps its claim.
    [HttpPost("jobs/{jobId:guid}/heartbeat")]
    [Authorize(Policy = CollectorPolicies.AnyDevice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Heartbeat(Guid jobId, CancellationToken ct)
    {
        return await jobs.HeartbeatAsync(CurrentDeviceId, jobId, ct)
            ? NoContent()
            : Conflict(new { error = "This job is not claimed by this device." });
    }

    [HttpPost("jobs/{jobId:guid}/complete")]
    [Authorize(Policy = CollectorPolicies.AnyDevice)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteJob(
        Guid jobId,
        [FromBody] CompleteCollectorJobRequest request,
        CancellationToken ct)
    {
        var result = await jobs.CompleteAsync(CurrentDeviceId, jobId, request, ct);
        return result switch
        {
            CollectorCompletionResult.Completed or CollectorCompletionResult.AlreadyCompleted => Ok(new { status = "Completed" }),
            CollectorCompletionResult.NotClaimedByDevice => Conflict(new { error = "This job is not claimed by this device." }),
            _ => NotFound(new { error = "Job not found." }),
        };
    }

    // The approved advertiser identities the app should collect.
    [HttpGet("identities")]
    [Authorize(Policy = CollectorPolicies.AdsIngest)]
    [ProducesResponseType(typeof(CollectorIdentitiesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentities(CancellationToken ct)
    {
        var meta = await (
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
                i.BlockedUntil)).ToListAsync(ct);

        var google = await (
            from i in readContext.Query<AdvertiserIdentity>()
            join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
            where i.Platform == AdPlatform.Google && i.Status == AdvertiserIdentityStatus.Approved
            select new GoogleIdentityDto(
                i.Id,
                i.CompetitorId,
                c.Key,
                c.Name,
                i.ExternalId,
                i.Name,
                i.Status.ToString(),
                i.BlockedUntil)).ToListAsync(ct);

        return Ok(new CollectorIdentitiesResponse(meta, google));
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/products")]
    [Authorize(Policy = CollectorPolicies.StoreIngest)]
    [ProducesResponseType(typeof(IngestBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IngestProducts(
        Guid runId,
        string slug,
        [FromBody] IngestProductsBatchRequest request,
        CancellationToken ct)
    {
        if (request?.Products == null)
        {
            return BadRequest(new { error = "Products payload is required." });
        }

        var claimError = await CheckStoreClaimAsync(runId, slug, ct);
        if (claimError is not null)
        {
            return claimError;
        }

        var result = await productIngestion.IngestBatchAsync(runId, slug, request.Page, request.TotalPages, request.Products, ct);
        return Ok(result);
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/complete")]
    [Authorize(Policy = CollectorPolicies.StoreIngest)]
    [ProducesResponseType(typeof(CompleteCompetitorResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteCompetitor(
        Guid runId,
        string slug,
        [FromBody] CompleteCompetitorRunRequest request,
        CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Complete payload is required." });
        }

        var claimError = await CheckStoreClaimAsync(runId, slug, ct);
        if (claimError is not null)
        {
            return claimError;
        }

        var result = await productIngestion.CompleteCompetitorAsync(
            runId,
            slug,
            request.Status,
            request.TotalPages,
            request.TotalProducts,
            request.Error,
            ct);
        return Ok(result);
    }

    [HttpPost("ads/meta")]
    [Authorize(Policy = CollectorPolicies.AdsIngest)]
    [ProducesResponseType(typeof(AdsIngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestMetaAds([FromBody] MetaAdsIngestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PageId))
        {
            return BadRequest(new { error = "PageId is required." });
        }

        return Ok(await adsIngestion.IngestMetaPageAsync(request, ct));
    }

    [HttpPost("ads/google")]
    [Authorize(Policy = CollectorPolicies.AdsIngest)]
    [ProducesResponseType(typeof(AdsIngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestGoogleAds([FromBody] GoogleAdsIngestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AdvertiserId))
        {
            return BadRequest(new { error = "AdvertiserId is required." });
        }

        return Ok(await adsIngestion.IngestGoogleAdvertiserAsync(request, ct));
    }

    // "Collect ads now" from the desktop app: queues the Meta and Google ads jobs for this batch.
    [HttpPost("ads-now")]
    [Authorize(Policy = CollectorPolicies.AnyDevice)]
    [ProducesResponseType(typeof(CollectorRunPlan), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> AdsNow(CancellationToken ct)
    {
        return Accepted(await jobs.PlanAdsOnlyAsync(ct));
    }

    // Sign out of the desktop app: revokes this device's own token.
    [HttpPost("unpair")]
    [Authorize(Policy = CollectorPolicies.AnyDevice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unpair(CancellationToken ct)
    {
        await sender.Send(new RevokeCollectorDeviceCommand(CurrentDeviceId), ct);
        return NoContent();
    }

    // A device may only write a competitor's products for the run and competitor it currently claims.
    private async Task<IActionResult?> CheckStoreClaimAsync(Guid runId, string slug, CancellationToken ct)
    {
        var competitorId = await readContext.Query<Competitor>()
            .Where(c => c.Key == slug)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (competitorId is null)
        {
            return NotFound(new { error = $"Competitor '{slug}' was not found." });
        }

        var state = await jobs.GetStoreJobStateAsync(runId, competitorId.Value, ct);
        if (state is null || !state.Claimed || state.ClaimedByDeviceId != CurrentDeviceId)
        {
            return Conflict(new { error = "This competitor is not claimed by this device for this run." });
        }

        return null;
    }
}

public sealed record CollectorIdentitiesResponse(
    IReadOnlyList<MetaIdentityDto> Meta,
    IReadOnlyList<GoogleIdentityDto> Google);
