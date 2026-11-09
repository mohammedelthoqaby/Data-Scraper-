namespace LazyBean.Infrastructure.Ai.Providers;

/// <summary>One concrete LLM provider (Groq, Gemini). Routing and fallback live in <see cref="AiClient"/>.</summary>
public interface IAiProvider
{
    /// <summary>Provider key: "groq" or "gemini".</summary>
    string Name { get; }

    Task<AiProviderResponse> SendAsync(AiProviderRequest request, string apiKey, string model, CancellationToken ct);
}

public sealed record AiProviderRequest(string SystemPrompt, string UserPrompt, int MaxOutputTokens);

public sealed record AiProviderResponse(string Text, int TotalTokens, AiRateLimitInfo? RateLimit);

/// <summary>Rate-limit state reported by the provider's response headers (when present).</summary>
public sealed record AiRateLimitInfo(
    int? RemainingRequests,
    TimeSpan? ResetRequests,
    int? RemainingTokens,
    TimeSpan? ResetTokens);

public enum AiProviderErrorKind
{
    RateLimited,
    InvalidKey,
    ServerError,
    Timeout,
    Network,
    BadRequest,
}

/// <summary>
/// Provider call failure. The message is already sanitized (no key, no query secrets)
/// and is safe to log and to show.
/// </summary>
public sealed class AiProviderException(
    AiProviderErrorKind kind,
    string message,
    int? statusCode = null,
    TimeSpan? retryAfter = null,
    AiRateLimitInfo? rateLimit = null) : Exception(message)
{
    public AiProviderErrorKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public AiRateLimitInfo? RateLimit { get; } = rateLimit;
}
