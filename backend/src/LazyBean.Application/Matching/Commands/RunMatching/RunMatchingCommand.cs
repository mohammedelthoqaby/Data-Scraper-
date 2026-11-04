using LazyBean.Application.Matching;
using MediatR;

namespace LazyBean.Application.Matching.Commands.RunMatching;

public record RunMatchingCommand : IRequest<MatchingResult>;

public class RunMatchingCommandHandler(IProductMatcher matcher) : IRequestHandler<RunMatchingCommand, MatchingResult>
{
    public Task<MatchingResult> Handle(RunMatchingCommand request, CancellationToken ct) =>
        matcher.MatchAllAsync(ct);
}
