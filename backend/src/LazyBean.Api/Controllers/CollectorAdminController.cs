using LazyBean.Api.Common;
using LazyBean.Application.Collector.Commands.RevokeCollectorDevice;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Collector.Queries.GetCollectorDevices;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LazyBean.Api.Controllers;

// Dashboard side of the collector: connection status, paired devices, run job status, "Collect ads
// now", and the desktop app download. Cookie-authenticated only, and outside /api/collector so a
// device token cannot reach it.
[ApiController]
[Route("api/collector-admin")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class CollectorAdminController(
    ISender sender,
    ICollectorJobService jobs,
    CollectorAppPackage appPackage) : ControllerBase
{

    [HttpGet("status")]
    [ProducesResponseType(typeof(CollectorStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectorStatusDto>> GetStatus(CancellationToken ct)
    {
        return Ok(await jobs.GetStatusAsync(ct));
    }

    [HttpGet("devices")]
    [ProducesResponseType(typeof(IReadOnlyList<CollectorDeviceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CollectorDeviceDto>>> GetDevices(CancellationToken ct)
    {
        return Ok(await sender.Send(new GetCollectorDevicesQuery(), ct));
    }

    [HttpDelete("devices/{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeDevice(Guid deviceId, CancellationToken ct)
    {
        await sender.Send(new RevokeCollectorDeviceCommand(deviceId), ct);
        return NoContent();
    }

    // Dashboard "Collect ads now": Meta and Google jobs with no run.
    [HttpPost("collect-ads-now")]
    [ProducesResponseType(typeof(CollectorRunPlan), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CollectAdsNow(CancellationToken ct)
    {
        var plan = await jobs.PlanAdsOnlyAsync(ct);
        return Accepted(plan);
    }

    [HttpGet("runs/{runId:guid}/jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<CollectorJobSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CollectorJobSummaryDto>>> GetRunJobs(Guid runId, CancellationToken ct)
    {
        return Ok(await jobs.GetRunJobsAsync(runId, ct));
    }

    // Whether the desktop installer is published, with the SHA-256 computed at startup.
    [HttpGet("app-info")]
    [ProducesResponseType(typeof(CollectorAppInfoResponse), StatusCodes.Status200OK)]
    public IActionResult GetAppInfo() =>
        Ok(new CollectorAppInfoResponse(appPackage.Available, appPackage.DownloadUrl, appPackage.SizeBytes, appPackage.Sha256));
}

public sealed record CollectorAppInfoResponse(bool Available, string? DownloadUrl, long? SizeBytes, string? Sha256);
