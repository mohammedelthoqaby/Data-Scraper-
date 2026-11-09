using LazyBean.Application.Ai.Commands;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Ai.Queries;
using LazyBean.Application.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/settings/ai")]
[EnableRateLimiting("api")]
public class AiSettingsController(ISender sender) : ControllerBase
{
    private bool IsAdmin()
    {
        // Must be authenticated and admin user (default username is admin)
        return User.Identity?.IsAuthenticated == true;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiSettingsDto>> GetSettings(CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        var result = await sender.Send(new GetAiSettingsQuery(), ct);
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(AiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiSettingsDto>> UpdateSettings([FromBody] UpdateAiSettingsRequestDto body, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        var result = await sender.Send(new UpdateAiSettingsCommand(
            body.IsEnabled,
            body.ApiKey,
            body.ModelName,
            body.DailyRequestCap,
            body.CreativeVisionEnabled,
            body.CreativeVisionDailyCap), ct);

        return Ok(result);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteSettings(CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        await sender.Send(new DeleteAiSettingsCommand(), ct);
        return NoContent();
    }

    [HttpPost("test")]
    [ProducesResponseType(typeof(AiTestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiTestResultDto>> TestConnection([FromBody] TestAiConnectionRequestDto? body, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        var result = await sender.Send(new TestAiConnectionCommand(body?.ApiKey, body?.ModelName), ct);
        return Ok(result);
    }

    [HttpPost("triage")]
    [ProducesResponseType(typeof(AiTriageStats), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiTriageStats>> RunTriage([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        var result = await sender.Send(new TriageReviewQueueCommand(limit), ct);
        return Ok(result);
    }
}
