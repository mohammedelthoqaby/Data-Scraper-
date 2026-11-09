namespace LazyBean.Application.Ai;

/// <summary>
/// Thrown when no AI provider can serve a request right now (disabled, not configured,
/// quota or rate limit reached, or every provider failing). Callers treat it as a
/// graceful stop for AI work and must never fail the surrounding run.
/// </summary>
public sealed class AiUnavailableException(string message, bool quotaReached) : Exception(message)
{
    /// <summary>True when the stop was caused by a quota, rate limit or daily cap.</summary>
    public bool QuotaReached { get; } = quotaReached;
}
