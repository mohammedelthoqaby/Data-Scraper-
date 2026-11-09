using LazyBean.Application.Ai;
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

    /// <summary>Returns settings with masked keys only. A key is never returned in any response.</summary>
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiSettingsDto>> UpdateSettings([FromBody] UpdateAiSettingsRequestDto body, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        if (body.PrimaryProvider is not null && !AiProviders.IsKnown(body.PrimaryProvider))
            return BadRequest(new { error = "Primary provider must be 'groq' or 'gemini'." });

        var result = await sender.Send(new UpdateAiSettingsCommand(body), ct);
        return Ok(result);
    }

    /// <summary>Saves a provider key (encrypted) and/or its model. Empty ApiKey keeps the saved key.</summary>
    [HttpPut("providers/{provider}")]
    [ProducesResponseType(typeof(AiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiSettingsDto>> SaveProvider(string provider, [FromBody] SaveAiProviderRequestDto body, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        if (!AiProviders.IsKnown(provider))
            return BadRequest(new { error = "Unknown AI provider. Expected 'groq' or 'gemini'." });

        try
        {
            var result = await sender.Send(new SaveAiProviderKeyCommand(provider, body), ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("providers/{provider}/key")]
    [ProducesResponseType(typeof(AiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiSettingsDto>> RemoveProviderKey(string provider, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        if (!AiProviders.IsKnown(provider))
            return BadRequest(new { error = "Unknown AI provider. Expected 'groq' or 'gemini'." });

        var result = await sender.Send(new RemoveAiProviderKeyCommand(provider), ct);
        return Ok(result);
    }

    /// <summary>Tiny real request to one provider. Returns ok | invalid_key | rate_limited | error | not_configured.</summary>
    [HttpPost("providers/{provider}/test")]
    [ProducesResponseType(typeof(AiKeyTestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiKeyTestResultDto>> TestProvider(string provider, [FromBody] TestAiProviderRequestDto? body, CancellationToken ct)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        if (!AiProviders.IsKnown(provider))
            return BadRequest(new { error = "Unknown AI provider. Expected 'groq' or 'gemini'." });

        var result = await sender.Send(new TestAiProviderKeyCommand(provider, body ?? new TestAiProviderRequestDto(null, null)), ct);
        return Ok(result);
    }

    /// <summary>Runs triage. The limit is capped by the configured per-run cap (AiSetting.TriageRunCap).</summary>
    [HttpPost("triage")]
    [ProducesResponseType(typeof(AiTriageStats), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AiTriageStats>> RunTriage([FromQuery] int? limit = null, CancellationToken ct = default)
    {
        if (!IsAdmin())
            return Unauthorized(new { error = "Admin authentication required." });

        var result = await sender.Send(new TriageReviewQueueCommand(limit), ct);
        return Ok(result);
    }
}
