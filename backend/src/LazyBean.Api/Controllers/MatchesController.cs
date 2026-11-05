using LazyBean.Application.Matching;
using LazyBean.Application.Matching.Commands.CreateMatch;
using LazyBean.Application.Matching.Commands.RunMatching;
using LazyBean.Application.Matching.Commands.SubmitMatchDecision;
using LazyBean.Application.Matching.Commands.SubmitMatchDecisions;
using LazyBean.Application.Matching.Queries.GetMatchAudit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

public record MatchDecisionRequest(string Decision, string? Notes = null);

[ApiController]
[Route("api/matches")]
[EnableRateLimiting("api")]
public class MatchesController(ISender sender) : ControllerBase
{
    [HttpGet("audit")]
    [ProducesResponseType(typeof(IReadOnlyList<MatchAuditItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MatchAuditItemDto>>> GetAuditMatches(CancellationToken ct)
    {
        var result = await sender.Send(new GetMatchAuditQuery(), ct);
        return Ok(result);
    }
    [HttpPost("decisions")]
    [ProducesResponseType(typeof(BulkDecisionResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BulkDecisionResultDto>> SubmitDecisions(
        [FromBody] SubmitMatchDecisionsCommand command,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/decision")]
    [ProducesResponseType(typeof(DecisionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DecisionResultDto>> SubmitDecision(
        Guid id,
        [FromBody] MatchDecisionRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new SubmitMatchDecisionCommand(id, request.Decision, request.Notes), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(MatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MatchResultDto>> CreateMatch(
        [FromBody] CreateMatchCommand command,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("run")]
    [ProducesResponseType(typeof(MatchingResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchingResult>> RunMatching(
        [FromServices] IMatchingQueue matchingQueue,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<MatchingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new MatchingJob(null, tcs);
        await matchingQueue.EnqueueAsync(job, ct);

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            var result = await tcs.Task;
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }
}
