using LazyBean.Application.Alerts.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[EnableRateLimiting("api")]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;

    public AlertsController(IAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AlertDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlertDto>>> GetAll(
        [FromQuery] AlertSeverity? severity,
        [FromQuery] AlertStatus? status,
        [FromQuery] bool? isBestseller,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var alerts = await _alertService.GetAlertsAsync(severity, status, isBestseller, search, ct);
        return Ok(alerts);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(AlertSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AlertSummaryDto>> GetSummary(CancellationToken ct)
    {
        var summary = await _alertService.GetAlertSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("health")]
    [ProducesResponseType(typeof(DataHealthDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DataHealthDto>> GetHealth(CancellationToken ct)
    {
        var health = await _alertService.GetDataHealthAsync(ct);
        return Ok(health);
    }

    [HttpPost("{id:guid}/action")]
    [ProducesResponseType(typeof(AlertDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AlertDto>> PerformAction(
        Guid id,
        [FromBody] AlertActionRequest request,
        CancellationToken ct)
    {
        var user = User.Identity?.Name ?? "manager";
        var result = await _alertService.PerformActionAsync(id, request.Action, request.Note, user, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertHistoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlertHistoryDto>>> GetHistory(Guid id, CancellationToken ct)
    {
        var history = await _alertService.GetHistoryAsync(id, ct);
        return Ok(history);
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlertDto>>> Sync(CancellationToken ct)
    {
        var result = await _alertService.SyncAlertsAsync(ct);
        return Ok(result);
    }
}

[ApiController]
[Route("api/settings/alerts")]
[EnableRateLimiting("api")]
public class AlertSettingsController : ControllerBase
{
    private readonly IAlertService _alertService;

    public AlertSettingsController(IAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AlertSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AlertSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await _alertService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut]
    [ProducesResponseType(typeof(AlertSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AlertSettingsDto>> Update(
        [FromBody] AlertSettingsDto dto,
        CancellationToken ct)
    {
        var updated = await _alertService.UpdateSettingsAsync(dto, ct);
        return Ok(updated);
    }
}
