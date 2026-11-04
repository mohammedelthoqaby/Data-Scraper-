using MediatR;

namespace LazyBean.Application.Matching.Commands.SubmitMatchDecisions;

public sealed record SubmitMatchDecisionsCommand(
    IReadOnlyList<MatchDecisionItemDto> Decisions) : IRequest<BulkDecisionResultDto>;

public sealed record MatchDecisionItemDto(
    Guid Id,
    string Decision,
    string? Notes = null);

public sealed record BulkDecisionResultDto(
    int TotalProcessed,
    int ApprovedCount,
    int RejectedCount,
    IReadOnlyList<DecisionItemSummaryDto> Results);

public sealed record DecisionItemSummaryDto(
    Guid Id,
    string Status,
    string Decision);
