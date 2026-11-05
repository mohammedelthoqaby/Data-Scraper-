namespace LazyBean.Application.Collection.Orchestration;

public interface IGitHubWorkflowDispatcher
{
    Task<GitHubDispatchResult> DispatchCollectorAsync(Guid runId, IReadOnlyList<string> competitorSlugs, CancellationToken ct = default);
}

public record GitHubDispatchResult(
    bool Success,
    int? StatusCode,
    string? ErrorMessage,
    string? ActionsUrl);
