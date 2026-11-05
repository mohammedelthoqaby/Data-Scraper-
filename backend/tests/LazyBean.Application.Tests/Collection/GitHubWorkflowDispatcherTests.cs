using System.Net;
using System.Text.Json;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Infrastructure.Collection.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class GitHubWorkflowDispatcherTests
{
    private class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task DispatchCollectorAsync_SendsValidPostRequest_ReturnsSuccessAndActionsUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GitHubOptions
        {
            DispatchToken = "ghp_testToken1234567890",
            Repo = "mohammedelthoqaby/Data-Scraper-",
            Ref = "main",
            WorkflowFile = "github-collect.yml"
        });

        var dispatcher = new GitHubWorkflowDispatcher(httpClient, options, NullLogger<GitHubWorkflowDispatcher>.Instance);
        var runId = Guid.NewGuid();
        var slugs = new[] { "strong-coffee" };

        var result = await dispatcher.DispatchCollectorAsync(runId, slugs);

        Assert.True(result.Success);
        Assert.Equal(204, result.StatusCode);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("https://github.com/mohammedelthoqaby/Data-Scraper-/actions/workflows/github-collect.yml", result.ActionsUrl);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://api.github.com/repos/mohammedelthoqaby/Data-Scraper-/actions/workflows/github-collect.yml/dispatches",
            capturedRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_testToken1234567890", capturedRequest.Headers.Authorization?.Parameter);
        Assert.Contains("application/vnd.github+json", capturedRequest.Headers.Accept.ToString());
        Assert.Equal("2022-11-28", capturedRequest.Headers.GetValues("X-GitHub-Api-Version").FirstOrDefault());

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal("main", doc.RootElement.GetProperty("ref").GetString());
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.Equal(runId.ToString(), inputs.GetProperty("run_id").GetString());
        Assert.Equal("strong-coffee", inputs.GetProperty("slug").GetString());
        Assert.Equal("strong-coffee", inputs.GetProperty("slugs").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchCollectorAsync_MissingToken_ReturnsFailureWithoutCallingHttp(string? missingToken)
    {
        var handlerCalled = false;
        var handler = new TestHttpMessageHandler(_ =>
        {
            handlerCalled = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GitHubOptions
        {
            DispatchToken = missingToken,
            Repo = "mohammedelthoqaby/Data-Scraper-",
            Ref = "main"
        });

        var dispatcher = new GitHubWorkflowDispatcher(httpClient, options, NullLogger<GitHubWorkflowDispatcher>.Instance);
        var result = await dispatcher.DispatchCollectorAsync(Guid.NewGuid(), new[] { "strong-coffee" });

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.False(handlerCalled);
        Assert.Contains("GitHub__DispatchToken is not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchCollectorAsync_Unauthorized401_ReturnsSanitizedErrorWithoutLeakingToken()
    {
        const string secretToken = "ghp_super_secret_never_leak_this";
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized",
            Content = new StringContent("{\"message\":\"Bad credentials\"}")
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GitHubOptions
        {
            DispatchToken = secretToken,
            Repo = "mohammedelthoqaby/Data-Scraper-",
            Ref = "main"
        });

        var dispatcher = new GitHubWorkflowDispatcher(httpClient, options, NullLogger<GitHubWorkflowDispatcher>.Instance);
        var result = await dispatcher.DispatchCollectorAsync(Guid.NewGuid(), new[] { "strong-coffee" });

        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("HTTP 401", result.ErrorMessage);
        Assert.DoesNotContain(secretToken, result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchCollectorAsync_Forbidden403_ReturnsSanitizedError()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = "Forbidden",
            Content = new StringContent("{\"message\":\"Resource not accessible by personal access token\"}")
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GitHubOptions
        {
            DispatchToken = "ghp_someToken",
            Repo = "mohammedelthoqaby/Data-Scraper-",
            Ref = "main"
        });

        var dispatcher = new GitHubWorkflowDispatcher(httpClient, options, NullLogger<GitHubWorkflowDispatcher>.Instance);
        var result = await dispatcher.DispatchCollectorAsync(Guid.NewGuid(), new[] { "strong-coffee" });

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("HTTP 403", result.ErrorMessage);
        Assert.Contains("Resource not accessible", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchCollectorAsync_MultipleCompetitorSlugs_FormatsCommaSeparatedInputs()
    {
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GitHubOptions
        {
            DispatchToken = "ghp_multiTestToken",
            Repo = "mohammedelthoqaby/Data-Scraper-",
            Ref = "main"
        });

        var dispatcher = new GitHubWorkflowDispatcher(httpClient, options, NullLogger<GitHubWorkflowDispatcher>.Instance);
        var runId = Guid.NewGuid();
        var result = await dispatcher.DispatchCollectorAsync(runId, new[] { "strong-coffee", "coffee-and-cream" });

        Assert.True(result.Success);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.Equal(runId.ToString(), inputs.GetProperty("run_id").GetString());
        Assert.Equal("strong-coffee", inputs.GetProperty("slug").GetString());
        Assert.Equal("strong-coffee,coffee-and-cream", inputs.GetProperty("slugs").GetString());
    }
}
