using LazyBean.Application.Runs.Queries.GetPriceMatrix;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/price-matrix")]
[EnableRateLimiting("api")]
public class PriceMatrixController(ISender sender) : ControllerBase
{
    [HttpGet("latest")]
    [HttpGet("")]
    [ProducesResponseType(typeof(PriceMatrixResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriceMatrixResponseDto>> GetLatest(CancellationToken ct)
    {
        var result = await sender.Send(new GetPriceMatrixQuery(null), ct);
        return Ok(result);
    }

    [HttpGet("latest/export")]
    [HttpGet("latest.csv")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportLatest(CancellationToken ct)
    {
        var matrix = await sender.Send(new GetPriceMatrixQuery(null), ct);
        var csvBytes = PriceMatrixCsvExporter.ExportToCsv(matrix);
        return File(csvBytes, "text/csv; charset=utf-8", "price-matrix-latest.csv");
    }
}
