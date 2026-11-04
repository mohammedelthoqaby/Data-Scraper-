using LazyBean.Application.Ads.Commands.SyncAds;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

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
