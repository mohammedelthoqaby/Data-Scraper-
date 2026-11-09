using System.Text.Json;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Ai;

/// <summary>Settings persistence: keys are encrypted, masked on every read, and never returned.</summary>
public class AiSettingsStoreTests
{
    private static AiSettingsStore CreateStore(IOptions<AiOptions> options, AiKeyProtector? protector = null, LazyBeanDbContext? db = null) =>
        new(db ?? CreateDb(), protector ?? CreateProtector(), options, NullLogger<AiSettingsStore>.Instance);

    [Fact]
    public async Task SaveKey_ReturnsMaskedFormOnly_AndNeverTheRawKey()
    {
        var db = CreateDb();
        var protector = CreateProtector();
        var store = CreateStore(CreateOptions(groqKey: null, geminiKey: null), protector, db);

        var dto = await store.SaveProviderAsync("groq", new SaveAiProviderRequestDto(FakeGroqKey, "openai/gpt-oss-120b"));

        var groq = dto.Providers.Single(p => p.Provider == "groq");
        Assert.True(groq.IsConfigured);
        Assert.Equal("database", groq.KeySource);
        Assert.Equal("gsk_••••1234", groq.MaskedApiKey);
        Assert.Equal("openai/gpt-oss-120b", groq.ModelName);

        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(FakeGroqKey, json);
        Assert.DoesNotContain("test_fake_key", json);

        // Stored encrypted: the ciphertext differs from the key and decrypts back to it server-side only.
        var row = await db.AiProviderSettings.AsNoTracking().SingleAsync(p => p.Provider == "groq");
        Assert.NotNull(row.EncryptedApiKey);
        Assert.NotEqual(FakeGroqKey, row.EncryptedApiKey);
        Assert.Equal(FakeGroqKey, protector.DecryptKey(row.EncryptedApiKey!));
    }

    [Fact]
    public async Task GetSettings_AfterSave_StillShowsOnlyTheMask()
    {
        var store = CreateStore(CreateOptions(groqKey: null, geminiKey: null));
        await store.SaveProviderAsync("gemini", new SaveAiProviderRequestDto(FakeGeminiKey, null));

        var dto = await store.GetAsync();

        var gemini = dto.Providers.Single(p => p.Provider == "gemini");
        Assert.Equal("AIza••••0001", gemini.MaskedApiKey);
        Assert.Equal(AiProviders.DefaultModel("gemini"), gemini.ModelName);
        Assert.DoesNotContain(FakeGeminiKey, JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task EnvironmentKey_OverridesSavedKey_AndIsMaskedFromTheEnvironmentValue()
    {
        const string envKey = "gsk_env_fake_key_9999";
        var store = CreateStore(CreateOptions(groqKey: envKey));
        await store.SaveProviderAsync("groq", new SaveAiProviderRequestDto("gsk_saved_fake_key_1111", null));

        var dto = await store.GetAsync();

        var groq = dto.Providers.Single(p => p.Provider == "groq");
        Assert.Equal("environment", groq.KeySource);
        Assert.Equal("gsk_••••9999", groq.MaskedApiKey);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(envKey, json);
        Assert.DoesNotContain("saved_fake_key", json);
    }

    [Fact]
    public async Task RemoveKey_ClearsTheSavedKeyAndMask()
    {
        var store = CreateStore(CreateOptions(groqKey: null, geminiKey: null));
        await store.SaveProviderAsync("groq", new SaveAiProviderRequestDto(FakeGroqKey, null));

        var dto = await store.RemoveProviderKeyAsync("groq");

        var groq = dto.Providers.Single(p => p.Provider == "groq");
        Assert.False(groq.IsConfigured);
        Assert.Equal("none", groq.KeySource);
        Assert.Null(groq.MaskedApiKey);
    }

    [Fact]
    public async Task SaveModel_AcceptsEditableModelIdsButRejectsWhitespace()
    {
        var store = CreateStore(CreateOptions(groqKey: null, geminiKey: null));

        var dto = await store.SaveProviderAsync("groq", new SaveAiProviderRequestDto(null, "qwen/qwen3.8-27b"));
        Assert.Equal("qwen/qwen3.8-27b", dto.Providers.Single(p => p.Provider == "groq").ModelName);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveProviderAsync("groq", new SaveAiProviderRequestDto(null, "two words")));
    }

    [Fact]
    public async Task UnknownProvider_IsRejected()
    {
        var store = CreateStore(CreateOptions());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveProviderAsync("openai", new SaveAiProviderRequestDto(FakeGroqKey, null)));
    }

    [Fact]
    public async Task UpdateSettings_SetsPrimaryProviderAndTriageCap()
    {
        var store = CreateStore(CreateOptions());

        var dto = await store.UpdateAsync(new UpdateAiSettingsRequestDto(
            IsEnabled: true,
            PrimaryProvider: "gemini",
            DailyRequestCap: 500,
            TriageRunCap: 300));

        Assert.True(dto.IsEnabled);
        Assert.Equal("gemini", dto.PrimaryProvider);
        Assert.Equal(500, dto.DailyRequestCap);
        Assert.Equal(300, dto.TriageRunCap);
    }

    [Fact]
    public async Task DefaultSettings_PrimaryIsGroqAndTriageCapIs200()
    {
        var store = CreateStore(CreateOptions());

        var dto = await store.GetAsync();

        Assert.False(dto.IsEnabled);
        Assert.Equal("groq", dto.PrimaryProvider);
        Assert.Equal(200, dto.TriageRunCap);
        Assert.Equal(["groq", "gemini"], dto.Providers.Select(p => p.Provider));
    }

    [Fact]
    public async Task SaveKey_ShorterThanPrefixLength_MasksWithoutPrefix()
    {
        var store = CreateStore(CreateOptions(groqKey: null, geminiKey: null));

        var dto = await store.SaveProviderAsync("groq", new SaveAiProviderRequestDto("abcdefgh", null));

        Assert.Equal("••••efgh", dto.Providers.Single(p => p.Provider == "groq").MaskedApiKey);
    }
}
