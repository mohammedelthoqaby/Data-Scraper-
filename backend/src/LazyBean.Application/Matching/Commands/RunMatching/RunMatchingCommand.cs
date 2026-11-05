using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching;
using MediatR;

namespace LazyBean.Application.Matching.Commands.RunMatching;

public record RunMatchingCommand : IRequest<MatchingResult>;

public class RunMatchingCommandHandler(IProductMatcher matcher, IAiTriageService triageService, IAlertService? alertService = null) : IRequestHandler<RunMatchingCommand, MatchingResult>
{
    public async Task<MatchingResult> Handle(RunMatchingCommand request, CancellationToken ct)
    {
        var result = await matcher.MatchAllAsync(ct);

        // After each matching pass, background AI triage of pending review pairs
        try
        {
            await triageService.TriagePendingReviewsAsync(100, ct);
        }
        catch
        {
            // AI triage failure should not fail matching
        }

        if (alertService != null)
        {
            try
            {
                await alertService.SyncAlertsAsync(ct);
            }
            catch
            {
                // Alert sync failure should not fail matching
            }
        }

        return result;
    }
}
