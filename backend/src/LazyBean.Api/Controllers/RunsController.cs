using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Offers.Queries.GetReferenceOffers;
using LazyBean.Application.Runs.Commands.CancelRun;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Application.Runs.Queries.GetCompetitorOffers;
using LazyBean.Application.Runs.Queries.GetPriceMatrix;
using LazyBean.Application.Runs.Queries.GetRunById;
using LazyBean.Application.Runs.Queries.GetRuns;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Api.Controllers;

public record StartRunRequest(IReadOnlyList<Guid>? CompetitorIds = null);
public record RedispatchResultDto(bool Success, string? Message, string? ActionsUrl);

[ApiController]
[Route("api/runs")]
[EnableRateLimiting("api")]
public class RunsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartRunRequest? request, CancellationToken ct)
    {
        var result = await sender.Send(new StartRunCommand(RunTrigger.Manual, request?.CompetitorIds), ct);
        if (result.AlreadyActive)
        {
            return Conflict(result);
        }
        return AcceptedAtAction(nameof(GetById), new { id = result.RunId }, result);
    }

    [HttpGet("active")]
    [HttpGet("active/progress")]
    [ProducesResponseType(typeof(RunProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetActive(
        [FromServices] ICollectionProgressTracker tracker,
        [FromServices] IReadDbContext db,
        CancellationToken ct)
    {
        var run = await db.Query<CollectionRun>()
            .Where(r => r.Status == CollectionRunStatus.Running || r.Status == CollectionRunStatus.Queued)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (run == null)
        {
            return NoContent();
        }

        var progress = tracker.GetProgress(run.Id) ?? tracker.GetActiveProgress();
        if (progress != null && progress.RunId == run.Id)
        {
            return Ok(progress);
        }

        var reconstructed = await ReconstructProgressFromDbAsync(run, db, ct);
        return Ok(reconstructed);
    }

    [HttpGet("{id:guid}/progress")]
    [ProducesResponseType(typeof(RunProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunProgressDto>> GetProgress(
        Guid id,
        [FromServices] ICollectionProgressTracker tracker,
        [FromServices] IReadDbContext db,
        CancellationToken ct)
    {
        var progress = tracker.GetProgress(id);
        if (progress != null)
        {
            return Ok(progress);
        }

        // Reconstruct from DB if completed / historical
        var run = await db.Query<CollectionRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run == null)
        {
            return NotFound();
        }

        var reconstructed = await ReconstructProgressFromDbAsync(run, db, ct);
        return Ok(reconstructed);
    }

    private static async Task<RunProgressDto> ReconstructProgressFromDbAsync(
        CollectionRun run,
        IReadDbContext db,
        CancellationToken ct)
    {
        var compRuns = await (
            from cr in db.Query<CompetitorRun>()
            join c in db.Query<Competitor>() on cr.CompetitorId equals c.Id
            where cr.RunId == run.Id
            select new CompetitorProgressDto(
                cr.CompetitorId,
                c.Key,
                c.Name,
                c.IsReference,
                cr.Status.ToString(),
                cr.Pages,
                cr.Pages,
                cr.Products,
                cr.CreatedAt,
                null,
                c.CollectionHost)
        ).ToListAsync(ct);

        if (compRuns.Count == 0)
        {
            var competitors = await db.Query<Competitor>()
                .Where(c => c.Enabled)
                .OrderBy(c => !c.IsReference)
                .ThenBy(c => c.Name)
                .ToListAsync(ct);

            compRuns = competitors.Select(c => new CompetitorProgressDto(
                c.Id,
                c.Key,
                c.Name,
                c.IsReference,
                run.Status == CollectionRunStatus.Running ? "Running" : "Queued",
                0,
                null,
                0,
                run.CreatedAt,
                null,
                c.CollectionHost)).ToList();
        }

        var phases = new List<PhaseProgressDto>();
        if (!string.IsNullOrWhiteSpace(run.PhasesJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(run.PhasesJson);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var st = el.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    var msg = el.TryGetProperty("message", out var m) ? m.GetString() : null;
                    var dur = el.TryGetProperty("durationMs", out var d) ? d.GetInt32() : (int?)null;
                    phases.Add(new PhaseProgressDto(name, st, msg, dur));
                }
            }
            catch
            {
            }
        }

        if (phases.Count == 0)
        {
            var defaultStatus = run.Status == CollectionRunStatus.Completed ? "completed" : "Queued";
            phases.Add(new PhaseProgressDto("classification", defaultStatus, null, null));
            phases.Add(new PhaseProgressDto("matching", defaultStatus, null, null));
            phases.Add(new PhaseProgressDto("ai_triage", defaultStatus, null, null));
            phases.Add(new PhaseProgressDto("alerts", defaultStatus, null, null));
        }

        int percent;
        if (run.Status == CollectionRunStatus.Completed)
        {
            percent = 100;
        }
        else if (run.Status == CollectionRunStatus.Partial)
        {
            percent = 90;
        }
        else if (run.Status == CollectionRunStatus.Queued)
        {
            percent = 0;
        }
        else
        {
            var compFinished = compRuns.Count(c => c.Status == "Completed" || c.Status == "Degraded" || c.Status == "Failed");
            var compWeight = compRuns.Count > 0 ? (double)compFinished / compRuns.Count : 0.0;
            var phaseFinished = phases.Count(p => p.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
            var phaseWeight = phases.Count > 0 ? (double)phaseFinished / phases.Count : 0.0;
            var calculated = (int)Math.Round((compWeight * 60.0) + (phaseWeight * 40.0));
            percent = Math.Clamp(calculated, 5, 95);
        }

        var isStalled = run.Status == CollectionRunStatus.Running &&
            (DateTimeOffset.UtcNow - (run.StartedAt ?? run.CreatedAt) > TimeSpan.FromMinutes(3));

        return new RunProgressDto(
            run.Id,
            run.Status.ToString(),
            percent,
            isStalled,
            $"Run {run.Id} is {run.Status}.",
            run.FinishedAt ?? run.StartedAt ?? run.CreatedAt,
            compRuns,
            phases,
            run.StartedAt ?? run.CreatedAt);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(CancelRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CancelRunResultDto>> Cancel(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new CancelRunCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/competitors/{slug}/redispatch")]
    [ProducesResponseType(typeof(RedispatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RedispatchResultDto>> Redispatch(
        Guid id,
        string slug,
        [FromServices] IGitHubWorkflowDispatcher dispatcher,
        [FromServices] ICollectionProgressTracker tracker,
        [FromServices] LazyBean.Infrastructure.Persistence.LazyBeanDbContext db,
        CancellationToken ct)
    {
        var comp = await db.Competitors.FirstOrDefaultAsync(c => c.Key == slug, ct);
        if (comp == null)
        {
            return NotFound(new { error = $"Competitor '{slug}' not found." });
        }

        var cr = await db.CompetitorRuns.FirstOrDefaultAsync(r => r.RunId == id && r.CompetitorId == comp.Id, ct);
        if (cr == null)
        {
            return NotFound(new { error = $"Run '{id}' does not have competitor '{slug}'." });
        }

        var result = await dispatcher.DispatchCollectorAsync(id, [slug], ct);
        if (result.Success)
        {
            cr.Status = CompetitorRunStatus.Running;
            cr.Errors = 0;
            await db.SaveChangesAsync(ct);
            tracker.UpdateCompetitorProgress(id, comp.Id, "Running", cr.Pages, null, cr.Products, "Re-dispatched GitHub Actions collector...");
            return Ok(new RedispatchResultDto(true, "Successfully dispatched GitHub Actions collector workflow.", result.ActionsUrl));
        }
        else
        {
            cr.Status = CompetitorRunStatus.Degraded;
            cr.Errors = 1;
            await db.SaveChangesAsync(ct);
            tracker.UpdateCompetitorProgress(id, comp.Id, "Degraded", cr.Pages, null, cr.Products, result.ErrorMessage);
            return Ok(new RedispatchResultDto(false, result.ErrorMessage, result.ActionsUrl));
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RunDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RunDto>>> GetAll(CancellationToken ct)
    {
        return Ok(await sender.Send(new GetRunsQuery(), ct));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunDto>> GetById(Guid id, CancellationToken ct)
    {
        return Ok(await sender.Send(new GetRunByIdQuery(id), ct));
    }

    [HttpGet("{id:guid}/price-matrix")]
    [ProducesResponseType(typeof(PriceMatrixResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PriceMatrixResponseDto>> GetPriceMatrix(Guid id, CancellationToken ct)
    {
        return Ok(await sender.Send(new GetPriceMatrixQuery(id), ct));
    }

    [HttpGet("{id:guid}/price-matrix/export")]
    [HttpGet("{id:guid}/export-matrix.csv")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportPriceMatrix(Guid id, CancellationToken ct)
    {
        var matrix = await sender.Send(new GetPriceMatrixQuery(id), ct);
        var csvBytes = PriceMatrixCsvExporter.ExportToCsv(matrix);
        return File(csvBytes, "text/csv; charset=utf-8", $"price-matrix-{id}.csv");
    }

    [HttpGet("{id:guid}/competitor-offers")]
    [ProducesResponseType(typeof(CompetitorOffersResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompetitorOffersResponseDto>> GetCompetitorOffers(
        Guid id,
        [FromQuery] Guid? competitorId,
        [FromQuery] string? competitorKey,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetCompetitorOffersQuery(id, competitorId, competitorKey, status), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/reference-offers")]
    [ProducesResponseType(typeof(ReferenceOffersResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReferenceOffersResponseDto>> GetReferenceOffers(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetReferenceOffersQuery(id), ct);
        return Ok(result);
    }
}
