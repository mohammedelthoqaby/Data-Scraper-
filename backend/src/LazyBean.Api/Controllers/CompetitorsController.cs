using LazyBean.Application.Competitors.Commands.CreateCompetitor;
using LazyBean.Application.Competitors.Commands.DeleteCompetitor;
using LazyBean.Application.Competitors.Commands.UpdateCompetitor;
using LazyBean.Application.Competitors.Dtos;
using LazyBean.Application.Competitors.Queries.GetCompetitorById;
using LazyBean.Application.Competitors.Queries.GetCompetitors;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/competitors")]
[Route("api/sources")]
[EnableRateLimiting("api")]
public class CompetitorsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CompetitorDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CompetitorDto>>> GetAll(CancellationToken ct)
    {
        var result = await sender.Send(new GetCompetitorsQuery(), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompetitorDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetCompetitorByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompetitorDto>> Create(CreateCompetitorCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompetitorDto>> Update(Guid id, [FromBody] PatchCompetitorRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateCompetitorCommand(
            id,
            body.Name,
            body.BaseUrl,
            body.Enabled,
            body.MinDelayMs,
            body.MaxConcurrency), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteCompetitorCommand(id), ct);
        return NoContent();
    }
}

public record PatchCompetitorRequest(
    string? Name = null,
    string? BaseUrl = null,
    bool? Enabled = null,
    int? MinDelayMs = null,
    int? MaxConcurrency = null);
