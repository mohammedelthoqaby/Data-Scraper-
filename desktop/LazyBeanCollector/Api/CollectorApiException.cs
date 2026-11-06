using System.Net;

namespace LazyBean.Collector.App.Api;

public enum ApiErrorKind
{
    // The server host name could not be resolved (usually a mistyped server URL). Never retried.
    ServerNotFound,

    // No connection to the server (offline, refused, reset). Never retried here; the worker loop waits and polls again.
    Offline,

    // The request took too long. Retried.
    Timeout,

    // 429 from the server (or Meta/Google through the server). Retried, honoring Retry-After.
    RateLimited,

    // 5xx from the server. Retried.
    ServerError,

    // 401: the device token is missing, invalid or revoked. The app must pair again.
    Unauthorized,

    // 403: the token is valid but lacks the scope for this call.
    Forbidden,

    // 409: the job is no longer claimed by this PC (its lease expired or it was revoked).
    Conflict,

    // 404: the run, competitor or job no longer exists.
    NotFound,

    // Any other 4xx: the request itself is wrong. Never retried.
    BadRequest,
}

public sealed class CollectorApiException(
    ApiErrorKind kind,
    string message,
    HttpStatusCode? statusCode = null,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    public ApiErrorKind Kind { get; } = kind;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    // Shown as-is to the user: says what happened and what to do next.
    public bool IsRetryable => Kind is ApiErrorKind.Timeout or ApiErrorKind.RateLimited or ApiErrorKind.ServerError;
}
