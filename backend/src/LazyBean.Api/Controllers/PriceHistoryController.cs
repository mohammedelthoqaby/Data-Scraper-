using LazyBean.Application.PriceHistory.Queries.GetPriceHistory;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/history/prices")]
[EnableRateLimiting("api")]
public class PriceHistoryController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PriceHistoryResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriceHistoryResponseDto>> GetPriceHistory(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? variantId,
        [FromQuery] Guid? competitorId,
        [FromQuery] string? competitorKey,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetPriceHistoryQuery(productId, variantId, competitorId, competitorKey, from, to, page, pageSize, false), ct);
        return Ok(result);
    }

    [HttpGet("export")]
    [HttpGet("/api/history/prices.csv")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPriceHistory(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? variantId,
        [FromQuery] Guid? competitorId,
        [FromQuery] string? competitorKey,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetPriceHistoryQuery(productId, variantId, competitorId, competitorKey, from, to, 1, 1, true), ct);
        var bytes = PriceHistoryCsvExporter.ExportToCsv(result);
        return File(bytes, "text/csv; charset=utf-8", "price-history.csv");
    }
}
