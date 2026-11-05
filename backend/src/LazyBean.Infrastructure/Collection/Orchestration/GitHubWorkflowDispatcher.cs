using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Collection.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class GitHubWorkflowDispatcher : IGitHubWorkflowDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<GitHubOptions> _options;
    private readonly ILogger<GitHubWorkflowDispatcher> _logger;

    public GitHubWorkflowDispatcher(
        HttpClient httpClient,
        IOptions<GitHubOptions> options,
        ILogger<GitHubWorkflowDispatcher> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<GitHubDispatchResult> DispatchCollectorAsync(
        Guid runId,
        IReadOnlyList<string> competitorSlugs,
        CancellationToken ct = default)
    {
        var options = _options.Value;
        var token = options.DispatchToken?.Trim();
        var repo = string.IsNullOrWhiteSpace(options.Repo) ? "mohammedelthoqaby/Data-Scraper-" : options.Repo.Trim();
        var targetRef = string.IsNullOrWhiteSpace(options.Ref) ? "main" : options.Ref.Trim();
        var workflowFile = string.IsNullOrWhiteSpace(options.WorkflowFile) ? "github-collect.yml" : options.WorkflowFile.Trim();

        var actionsUrl = $"https://github.com/{repo}/actions/workflows/{workflowFile}";

        if (string.IsNullOrWhiteSpace(token))
        {
            var err = "GitHub dispatch failed: GitHub__DispatchToken is not configured in MonsterASP environment variables.";
            _logger.LogWarning("GitHub workflow dispatch failed: GitHub__DispatchToken is not configured.");
            return new GitHubDispatchResult(false, null, err, actionsUrl);
        }

        var url = $"https://api.github.com/repos/{repo}/actions/workflows/{workflowFile}/dispatches";
        var slugString = string.Join(",", competitorSlugs);

        var payload = new
        {
            @ref = targetRef,
            inputs = new Dictionary<string, string>
            {
                ["run_id"] = runId.ToString(),
                ["slug"] = competitorSlugs.FirstOrDefault() ?? "strong-coffee",
                ["slugs"] = slugString
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LazyBean-Backend", "1.0"));

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully dispatched GitHub Actions workflow {Workflow} for RunId {RunId} ({Slugs}).",
                    workflowFile, runId, slugString);
                return new GitHubDispatchResult(true, (int)response.StatusCode, null, actionsUrl);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var sanitizedBody = responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
            sanitizedBody = sanitizedBody.Replace("\r", "").Replace("\n", " ").Trim();

            var statusCode = (int)response.StatusCode;
            var errorMsg = $"GitHub dispatch failed: HTTP {statusCode} ({response.ReasonPhrase}). {sanitizedBody}".Trim();

            // Crucial security requirement: NEVER log the token!
            _logger.LogWarning("GitHub dispatch failed with HTTP {StatusCode} for RunId {RunId}: {Message}",
                statusCode, runId, errorMsg);

            return new GitHubDispatchResult(false, statusCode, errorMsg, actionsUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exception while dispatching GitHub Actions workflow {Workflow} for RunId {RunId}.", workflowFile, runId);
            return new GitHubDispatchResult(false, null, $"GitHub dispatch failed: {ex.Message}", actionsUrl);
        }
    }
}
