using MediatR;

namespace LazyBean.Application.Matching.Commands.SubmitMatchDecision;

public record DecisionResultDto(Guid Id, string Status, string Decision);

public record SubmitMatchDecisionCommand(Guid Id, string Decision, string? Notes = null)
    : IRequest<DecisionResultDto>;
