using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Ai.RateLimiting;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Ai;

/// <summary>Router behaviour with fake provider clients: primary, fallback, graceful stop and key handling.</summary>
public class AiRouterTests
{
    private static async Task<LazyBeanDbContext> SeedSettingsAsync(string primary = "groq", bool enabled = true, int dailyCap = 1000)
    {
        var db = CreateDb();
        db.AiSettings.Add(new AiSetting
        {
            IsEnabled = enabled,
            PrimaryProvider = primary,
            DailyRequestCap = dailyCap,
            TriageRunCap = 200,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static AiClient CreateClient(
        LazyBeanDbContext db,
        IEnumerable<IAiProvider> providers,
        Microsoft.Extensions.Options.IOptions<AiOptions>? options = null)
    {
        var resolved = options ?? CreateOptions();
        return new AiClient(
            db,
            CreateProtector(),
            providers,
            new AiRateGate(resolved),
            resolved,
            NullLogger<AiClient>.Instance);
    }

    private static readonly AiJsonRequest SampleRequest = new("system", "user", 64);

    [Fact]
    public async Task CompleteJson_WhenGroqRateLimited_FallsBackToGemini()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => throw new AiProviderException(
            AiProviderErrorKind.RateLimited, "groq rate limit or quota reached (HTTP 429).", 429, TimeSpan.FromSeconds(30)));
        var gemini = new FakeProvider("gemini", () => new AiProviderResponse("{\"items\":[]}", 120, null));
        var client = CreateClient(db, [groq, gemini]);

        var result = await client.CompleteJsonAsync(SampleRequest);

        Assert.Equal("gemini", result.Provider);
        Assert.Equal("{\"items\":[]}", result.Text);
        Assert.Equal(1, groq.CallCount);
        Assert.Equal(1, gemini.CallCount);

        var groqRow = await db.AiProviderSettings.AsNoTracking().SingleAsync(p => p.Provider == "groq");
        Assert.NotNull(groqRow.Last429At);
        Assert.Contains("rate limit", groqRow.LastError);

        var usage = await db.AiProviderUsages.AsNoTracking().ToListAsync();
        Assert.Equal(1, usage.Single(u => u.Provider == "groq").RequestCount);
        Assert.Equal(1, usage.Single(u => u.Provider == "gemini").RequestCount);
    }

    [Fact]
    public async Task CompleteJson_WhenGroqRateLimited_BlocksGroqForRetryAfterWindow()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => throw new AiProviderException(
            AiProviderErrorKind.RateLimited, "rate limited", 429, TimeSpan.FromMinutes(5)));
        var gemini = new FakeProvider("gemini", () => new AiProviderResponse("{}", 10, null));
        var client = CreateClient(db, [groq, gemini]);

        await client.CompleteJsonAsync(SampleRequest);
        await client.CompleteJsonAsync(SampleRequest);

        // The second call must skip Groq during its cool-down instead of spending another request on it.
        Assert.Equal(1, groq.CallCount);
        Assert.Equal(2, gemini.CallCount);
    }

    [Fact]
    public async Task CompleteJson_WhenBothProvidersDown_ThrowsUnavailableWithQuotaReached()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => throw new AiProviderException(
            AiProviderErrorKind.RateLimited, "rate limited", 429));
        var gemini = new FakeProvider("gemini", () => throw new AiProviderException(
            AiProviderErrorKind.ServerError, "Google Gemini server error (HTTP 503).", 503));
        var client = CreateClient(db, [groq, gemini]);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteJsonAsync(SampleRequest));

        Assert.True(ex.QuotaReached);
        Assert.Equal("AI quota reached", ex.Message);
    }

    [Fact]
    public async Task CompleteJson_WhenBothProvidersFailWithoutQuota_ThrowsUnavailableWithoutQuotaFlag()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => throw new AiProviderException(AiProviderErrorKind.Timeout, "timed out"));
        var gemini = new FakeProvider("gemini", () => throw new AiProviderException(AiProviderErrorKind.ServerError, "server error"));
        var client = CreateClient(db, [groq, gemini]);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteJsonAsync(SampleRequest));

        Assert.False(ex.QuotaReached);
        Assert.Equal("AI providers unavailable", ex.Message);
    }

    [Fact]
    public async Task CompleteJson_WhenGroqHasNoKey_UsesGeminiDirectly()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => new AiProviderResponse("{\"groq\":true}", 5, null));
        var gemini = new FakeProvider("gemini", () => new AiProviderResponse("{\"gemini\":true}", 5, null));
        var client = CreateClient(db, [groq, gemini], CreateOptions(groqKey: null));

        var result = await client.CompleteJsonAsync(SampleRequest);

        Assert.Equal("gemini", result.Provider);
        Assert.Equal(0, groq.CallCount);
    }

    [Fact]
    public async Task CompleteJson_WhenDisabled_ThrowsUnavailableWithoutCallingAnyProvider()
    {
        using var db = await SeedSettingsAsync("groq", enabled: false);
        var groq = new FakeProvider("groq", () => new AiProviderResponse("{}", 1, null));
        var client = CreateClient(db, [groq]);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteJsonAsync(SampleRequest));

        Assert.False(ex.QuotaReached);
        Assert.Equal(0, groq.CallCount);
    }

    [Fact]
    public async Task CompleteJson_WhenDailyCapReached_StopsWithQuotaAndNoCall()
    {
        using var db = await SeedSettingsAsync("groq", dailyCap: 1);
        var groq = new FakeProvider("groq", () => new AiProviderResponse("{}", 10, null));
        var client = CreateClient(db, [groq]);

        await client.CompleteJsonAsync(SampleRequest);
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteJsonAsync(SampleRequest));

        Assert.True(ex.QuotaReached);
        Assert.Equal(1, groq.CallCount);
    }

    [Fact]
    public async Task CompleteJson_UsesEnvironmentKeyEvenWhenAKeyIsSaved()
    {
        using var db = await SeedSettingsAsync("groq");
        db.AiProviderSettings.Add(new AiProviderSetting
        {
            Provider = "groq",
            EncryptedApiKey = CreateProtector().EncryptKey("gsk_saved_fake_key_0000"),
            KeyLastFour = "0000",
        });
        await db.SaveChangesAsync();

        var groq = new FakeProvider("groq", () => new AiProviderResponse("{}", 1, null));
        var client = CreateClient(db, [groq], CreateOptions(groqKey: "gsk_env_fake_key_9999"));

        await client.CompleteJsonAsync(SampleRequest);

        Assert.Equal("gsk_env_fake_key_9999", Assert.Single(groq.ApiKeysUsed));
    }

    [Fact]
    public async Task TestProviderKey_WhenKeyRejected_ReturnsInvalidKeyWithoutEchoingTheKey()
    {
        using var db = await SeedSettingsAsync("groq");
        const string typedKey = "gsk_typed_fake_key_0000";
        var groq = new FakeProvider("groq", () => throw new AiProviderException(
            AiProviderErrorKind.InvalidKey, "groq rejected the API key (HTTP 401).", 401));
        var client = CreateClient(db, [groq]);

        var result = await client.TestProviderKeyAsync("groq", typedKey, null);

        Assert.Equal("invalid_key", result.Status);
        Assert.DoesNotContain(typedKey, result.Message);
        Assert.Equal(typedKey, Assert.Single(groq.ApiKeysUsed));
    }

    [Fact]
    public async Task TestProviderKey_WhenNoKeyAnywhere_ReturnsNotConfigured()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => new AiProviderResponse("{}", 1, null));
        var client = CreateClient(db, [groq], CreateOptions(groqKey: null));

        var result = await client.TestProviderKeyAsync("groq", null, null);

        Assert.Equal("not_configured", result.Status);
        Assert.Equal(0, groq.CallCount);
    }

    [Fact]
    public async Task TestProviderKey_WhenRateLimited_ReturnsRateLimitedStatus()
    {
        using var db = await SeedSettingsAsync("groq");
        var groq = new FakeProvider("groq", () => throw new AiProviderException(AiProviderErrorKind.RateLimited, "rate limited", 429));
        var client = CreateClient(db, [groq]);

        var result = await client.TestProviderKeyAsync("groq", null, null);

        Assert.Equal("rate_limited", result.Status);
    }

    [Fact]
    public async Task TriageReviewBatch_RoutesThroughFallbackAndTagsResultsWithModel()
    {
        using var db = await SeedSettingsAsync("groq");
        var pair = new AiReviewTriagePairRequest(Guid.NewGuid(), "Ref title", "Comp title");
        var reply = $"{{\"results\":[{{\"reviewId\":\"{pair.ReviewId}\",\"verdict\":\"same\",\"confidence\":0.9,\"reason\":\"match\"}}]}}";

        var groq = new FakeProvider("groq", () => throw new AiProviderException(AiProviderErrorKind.ServerError, "server error", 503));
        var gemini = new FakeProvider("gemini", () => new AiProviderResponse(reply, 80, null));
        var client = CreateClient(db, [groq, gemini]);

        var results = await client.TriageReviewBatchAsync([pair]);

        var single = Assert.Single(results);
        Assert.Equal("same", single.Verdict);
        Assert.Equal(pair.ReviewId, single.ReviewId);
        Assert.Equal(AiProviders.DefaultModel("gemini"), results[0].Model);
    }
}
