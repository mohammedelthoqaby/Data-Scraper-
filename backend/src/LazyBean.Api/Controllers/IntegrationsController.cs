using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetMetaIntegrationStatus;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[EnableRateLimiting("api")]
public class IntegrationsController(ISender sender) : ControllerBase
{
    [HttpGet("meta")]
    [ProducesResponseType(typeof(MetaIntegrationStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MetaIntegrationStatusDto>> GetMetaStatus(CancellationToken ct)
    {
        var result = await sender.Send(new GetMetaIntegrationStatusQuery(), ct);
        return Ok(result);
    }
}
