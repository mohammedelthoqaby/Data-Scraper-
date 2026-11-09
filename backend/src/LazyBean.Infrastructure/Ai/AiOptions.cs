namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Bound from the "Ai" configuration section. Environment variables override the
/// saved keys: Ai__Groq__ApiKey and Ai__Gemini__ApiKey.
/// Rate limits default to the free-tier numbers and can be adjusted per deployment.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProviderOptions Groq { get; set; } = new()
    {
        RequestsPerMinute = 30,
        TokensPerMinute = 8000,
        RequestsPerDay = 1000,
    };

    public AiProviderOptions Gemini { get; set; } = new()
    {
        RequestsPerMinute = 15,
        TokensPerMinute = 250000,
        RequestsPerDay = 1000,
    };

    /// <summary>Longest time a call waits for rate-limit budget before the provider is skipped.</summary>
    public int MaxRateWaitSeconds { get; set; } = 30;

    /// <summary>Review pairs per triage prompt.</summary>
    public int TriageBatchSize { get; set; } = 10;

    public AiProviderOptions ForProvider(string provider) =>
        string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase) ? Gemini : Groq;
}

public sealed class AiProviderOptions
{
    public string? ApiKey { get; set; }

    public string? BaseUrl { get; set; }

    public int RequestsPerMinute { get; set; }

    public int TokensPerMinute { get; set; }

    public int RequestsPerDay { get; set; }
}
