using System.Net;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Ai;

/// <summary>HTTP-level behaviour of the Groq and Gemini clients, using a scripted transport (no network).</summary>
public class AiProviderClientTests
{
    private const string GroqCompletion = "{\"choices\":[{\"message\":{\"content\":\"{\\\"results\\\":[]}\"}}],\"usage\":{\"total_tokens\":42}}";

    private const string GeminiCompletion = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"items\\\":[]}\"}]}}],\"usageMetadata\":{\"totalTokenCount\":77}}";

    private static readonly AiProviderRequest Request = new("system", "user", 128);

    private static GroqClient CreateGroq(ScriptedHttpHandler handler) =>
        new(new HttpClient(handler), CreateOptions(), NullLogger<GroqClient>.Instance);

    private static GeminiClient CreateGemini(ScriptedHttpHandler handler) =>
        new(new HttpClient(handler), CreateOptions(), NullLogger<GeminiClient>.Instance);

    [Fact]
    public async Task Groq_Success_ReturnsTextAndTokens_WithBearerAuth()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, GroqCompletion);

        var result = await CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None);

        Assert.Equal("{\"results\":[]}", result.Text);
        Assert.Equal(42, result.TotalTokens);
        Assert.Equal($"Bearer {FakeGroqKey}", handler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Groq_429WithResetHeader_ThrowsRateLimitedWithParsedWindow()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue((HttpStatusCode)429, "{\"error\":{\"message\":\"Rate limit reached\"}}", message =>
        {
            message.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "0");
            message.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", "2m59.56s");
        });

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.RateLimited, ex.Kind);
        Assert.Equal(429, ex.StatusCode);
        Assert.NotNull(ex.RetryAfter);
        Assert.InRange(ex.RetryAfter!.Value.TotalSeconds, 179.5, 179.6);
        Assert.Equal(0, ex.RateLimit?.RemainingRequests);
    }

    [Fact]
    public async Task Groq_429WithRetryAfterHeader_HonoursRetryAfter()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue((HttpStatusCode)429, "{}", message =>
        {
            message.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        });

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Fact]
    public async Task Groq_RateLimitHeaders_AreExposedOnSuccess()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, GroqCompletion, message =>
        {
            message.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "5000");
            message.Headers.TryAddWithoutValidation("x-ratelimit-reset-tokens", "7.66s");
        });

        var result = await CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None);

        Assert.Equal(5000, result.RateLimit?.RemainingTokens);
        Assert.InRange(result.RateLimit!.ResetTokens!.Value.TotalSeconds, 7.6, 7.7);
    }

    [Fact]
    public async Task Groq_401_ClassifiedAsInvalidKey()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API Key\"}}");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.InvalidKey, ex.Kind);
    }

    [Fact]
    public async Task Groq_500_ClassifiedAsServerError()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "upstream failure");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.ServerError, ex.Kind);
    }

    [Fact]
    public async Task Groq_BadRequestEchoingTheKey_MessageIsSanitized()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, $"{{\"error\":\"bad request for key {FakeGroqKey}\"}}");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGroq(handler).SendAsync(Request, FakeGroqKey, "openai/gpt-oss-120b", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.BadRequest, ex.Kind);
        Assert.DoesNotContain(FakeGroqKey, ex.Message);
        Assert.Contains("[redacted]", ex.Message);
    }

    [Fact]
    public async Task Gemini_Success_ReturnsCandidateText_KeyInHeaderNotUrl()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, GeminiCompletion);

        var result = await CreateGemini(handler).SendAsync(Request, FakeGeminiKey, "gemini-3.5-flash-lite", CancellationToken.None);

        Assert.Equal("{\"items\":[]}", result.Text);
        Assert.Equal(77, result.TotalTokens);

        var sent = handler.Requests[0];
        Assert.DoesNotContain(FakeGeminiKey, sent.RequestUri!.ToString());
        Assert.Contains("/models/gemini-3.5-flash-lite:generateContent", sent.RequestUri.ToString());
        Assert.True(sent.Headers.TryGetValues("x-goog-api-key", out var values));
        Assert.Contains(FakeGeminiKey, values);
    }

    [Fact]
    public async Task Gemini_ApiKeyInvalid400_ClassifiedAsInvalidKey()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGemini(handler).SendAsync(Request, FakeGeminiKey, "gemini-3.5-flash-lite", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.InvalidKey, ex.Kind);
        Assert.DoesNotContain(FakeGeminiKey, ex.Message);
    }

    [Fact]
    public async Task Gemini_429_ClassifiedAsRateLimited()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue((HttpStatusCode)429, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGemini(handler).SendAsync(Request, FakeGeminiKey, "gemini-3.5-flash-lite", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task Gemini_EmptyCandidates_IsTreatedAsServerErrorSoTheRouterFallsBack()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"candidates\":[]}");

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            CreateGemini(handler).SendAsync(Request, FakeGeminiKey, "gemini-3.5-flash-lite", CancellationToken.None));

        Assert.Equal(AiProviderErrorKind.ServerError, ex.Kind);
    }
}
