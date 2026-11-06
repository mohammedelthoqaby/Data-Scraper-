namespace LazyBean.Collector.App.Api;

// Retries only what can succeed on a second try: timeouts, 429 and 5xx. Name resolution failures,
// connection failures and 4xx responses are returned at once. At most `maxRetries` retries after
// the first attempt (so 3 attempts in total with the default of 2).
public sealed class RetryPolicy(int maxRetries = 2, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((d, ct) => Task.Delay(d, ct));

    public int MaxRetries { get; } = maxRetries;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct, bool retryServerErrors = true)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                return await operation(ct);
            }
            catch (CollectorApiException ex) when (ex.IsRetryable && (retryServerErrors || ex.Kind != ApiErrorKind.ServerError) && retry < MaxRetries)
            {
                var wait = ex.RetryAfter is { } serverAsked
                    ? (serverAsked > MaxRetryAfter ? MaxRetryAfter : serverAsked)
                    : BackoffFor(retry);
                await _delay(wait, ct);
            }
        }
    }

    // 2 s, then 5 s.
    public static TimeSpan BackoffFor(int retry) => retry switch
    {
        0 => TimeSpan.FromSeconds(2),
        _ => TimeSpan.FromSeconds(5),
    };
}
