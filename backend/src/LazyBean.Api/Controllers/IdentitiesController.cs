using LazyBean.Application.Ads.Commands.ApproveIdentity;
using LazyBean.Application.Ads.Commands.CreateIdentity;
using LazyBean.Application.Ads.Commands.DiscoverGoogleAdvertisers;
using LazyBean.Application.Ads.Commands.RejectIdentity;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetIdentities;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

public record DecisionBody(string? Notes = null);

[ApiController]
[Route("api/identities")]
[EnableRateLimiting("api")]
public class IdentitiesController(ISender sender) : ControllerBase
{
    [HttpPost("discover-google/{competitorId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<AdvertiserIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdvertiserIdentityDto>>> DiscoverGoogleAdvertisers(
        Guid competitorId,
        CancellationToken ct)
    {
        var result = await sender.Send(new DiscoverGoogleAdvertisersCommand(competitorId), ct);
        return Ok(result);
    }
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdvertiserIdentityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdvertiserIdentityDto>>> GetIdentities(
        [FromQuery] Guid? competitorId,
        [FromQuery] string? platform,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetIdentitiesQuery(competitorId, platform, status), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdvertiserIdentityDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdvertiserIdentityDto>> CreateIdentity(
        [FromBody] CreateIdentityCommand command,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return CreatedAtAction(nameof(GetIdentities), new { competitorId = result.CompetitorId }, result);
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(AdvertiserIdentityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdvertiserIdentityDto>> ApproveIdentity(
        Guid id,
        [FromBody] DecisionBody? body,
        CancellationToken ct)
    {
        var result = await sender.Send(new ApproveIdentityCommand(id, body?.Notes), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(AdvertiserIdentityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdvertiserIdentityDto>> RejectIdentity(
        Guid id,
        [FromBody] DecisionBody? body,
        CancellationToken ct)
    {
        var result = await sender.Send(new RejectIdentityCommand(id, body?.Notes), ct);
        return Ok(result);
    }
}
