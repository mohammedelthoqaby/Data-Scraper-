using LazyBean.Application.Matching.Dtos;
using LazyBean.Application.Matching.Queries.GetReviews;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/reviews")]
[EnableRateLimiting("api")]
public class ReviewsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ReviewQueueResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewQueueResponseDto>> GetAll(
        [FromQuery] ReviewStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        return Ok(await sender.Send(new GetReviewsQuery(status, page, pageSize), ct));
    }
}
