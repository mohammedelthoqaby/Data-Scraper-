using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching;
using LazyBean.Application.Matching.AiMatching;
using MediatR;

namespace LazyBean.Application.Matching.Commands.RunMatching;

public record RunMatchingCommand : IRequest<MatchingResult>;

/// <summary>Rule matching, then AI matching, then AI triage, then alerts. Only rule matching can fail this command.</summary>
public class RunMatchingCommandHandler(
    IProductMatcher matcher,
    IAiTriageService triageService,
    IAlertService? alertService = null,
    IAiMatchingService? aiMatching = null) : IRequestHandler<RunMatchingCommand, MatchingResult>
{
    public async Task<MatchingResult> Handle(RunMatchingCommand request, CancellationToken ct)
    {
        var result = await matcher.MatchAllAsync(ct);

        // AI matching decides the uncertain and missed pairs. Its failure must not fail matching.
        if (aiMatching != null)
        {
            try
            {
                await aiMatching.RunAsync(null, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // AI matching failure should not fail matching
            }
        }

        // After each matching pass, background AI triage of pending review pairs
        try
        {
            await triageService.TriagePendingReviewsAsync(null, ct);
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
