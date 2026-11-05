using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Overview.Queries.GetAttentionItems;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/overview")]
[EnableRateLimiting("api")]
public class OverviewController(ISender sender) : ControllerBase
{
    [HttpGet("attention")]
    [ProducesResponseType(typeof(IReadOnlyList<RankedAttentionItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<RankedAttentionItem>>> GetAttentionItems(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { error = "Authentication required." });

        var result = await sender.Send(new GetAttentionItemsQuery(), ct);
        return Ok(result);
    }
}
