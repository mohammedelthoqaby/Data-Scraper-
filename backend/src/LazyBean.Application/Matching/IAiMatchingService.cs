namespace LazyBean.Application.Matching.AiMatching;

/// <summary>
/// AI-assisted matching pass that runs after rule matching. It decides only the uncertain rule
/// matches and the missed matches. Rules keep the final say through hard guardrails, and a manual
/// decision is never overridden.
/// </summary>
public interface IAiMatchingService
{
    Task<AiMatchingStats> RunAsync(Guid? runId, CancellationToken ct = default);
}
