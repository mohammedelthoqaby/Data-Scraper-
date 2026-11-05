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

[ApiController]
[Route("api/runs")]
[EnableRateLimiting("api")]
public class RunsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Start([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartRunRequest? request, CancellationToken ct)
    {
        var result = await sender.Send(new StartRunCommand(RunTrigger.Manual, request?.CompetitorIds), ct);
        if (result.AlreadyActive)
        {
            return Ok(result);
        }
        return AcceptedAtAction(nameof(GetById), new { id = result.RunId }, result);
    }

    [HttpGet("active/progress")]
    [ProducesResponseType(typeof(RunProgressDto), StatusCodes.Status200OK)]
    public ActionResult<RunProgressDto?> GetActiveProgress([FromServices] ICollectionProgressTracker tracker)
    {
        var progress = tracker.GetActiveProgress();
        return Ok(progress);
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

        var compRuns = await (
            from cr in db.Query<CompetitorRun>()
            join c in db.Query<Competitor>() on cr.CompetitorId equals c.Id
            where cr.RunId == id
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
                null)
        ).ToListAsync(ct);

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

        var percent = run.Status == CollectionRunStatus.Completed ? 100 : (run.Status == CollectionRunStatus.Partial ? 90 : 0);
        return Ok(new RunProgressDto(
            run.Id,
            run.Status.ToString(),
            percent,
            false,
            null,
            run.FinishedAt ?? run.StartedAt ?? run.CreatedAt,
            compRuns,
            phases));
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(CancelRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CancelRunResultDto>> Cancel(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new CancelRunCommand(id), ct);
        return Ok(result);
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
