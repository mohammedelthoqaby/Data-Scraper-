using LazyBean.Application.Offers.Queries.GetReferenceOffers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/reference-offers")]
[EnableRateLimiting("api")]
public class OffersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ReferenceOffersResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReferenceOffersResponseDto>> GetReferenceOffers(
        [FromQuery] Guid? runId,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetReferenceOffersQuery(runId), ct);
        return Ok(result);
    }
}
