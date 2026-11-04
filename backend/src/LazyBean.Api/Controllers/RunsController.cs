using LazyBean.Application.Offers.Queries.GetReferenceOffers;
using LazyBean.Application.Runs.Commands.CancelRun;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Application.Runs.Queries.GetCompetitorOffers;
using LazyBean.Application.Runs.Queries.GetPriceMatrix;
using LazyBean.Application.Runs.Queries.GetRunById;
using LazyBean.Application.Runs.Queries.GetRuns;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

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
