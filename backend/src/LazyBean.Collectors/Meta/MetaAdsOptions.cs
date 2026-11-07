namespace LazyBean.Collectors.Meta;

// Timing knobs copied from meta_ads_collector.constants. Every wait goes through Delay so tests
// can run the full pagination and retry logic without sleeping.
public sealed record MetaAdsOptions
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // Session-level HTTP retries (429 and transport errors): DEFAULT_MAX_RETRIES / DEFAULT_RETRY_DELAY.
    public int HttpMaxRetries { get; init; } = 3;
    public TimeSpan HttpRetryDelayBase { get; init; } = TimeSpan.FromSeconds(2);

    // Bootstrap pause before the first GraphQL call (initialize): uniform 1.5-3.0 s.
    public TimeSpan BootstrapDelayMin { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan BootstrapDelayMax { get; init; } = TimeSpan.FromSeconds(3);

    // GraphQL error 1675004 / "rate limit" wait inside the client: 5 s + uniform 0-3 s.
    public TimeSpan GraphqlRateLimitWait { get; init; } = TimeSpan.FromSeconds(5);

    // Between pages of one search: DEFAULT_RATE_LIMIT_DELAY (2 s) + uniform 0-1 s jitter.
    public TimeSpan PageDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PageJitter { get; init; } = TimeSpan.FromSeconds(1);

    // Search-level retries in the collector: rate limit 5*n + uniform 1-3 s, session 2 s, transport 3*n s.
    public int SearchMaxRetries { get; init; } = 3;
    public TimeSpan RateLimitRetryBase { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan SessionRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TransportRetryBase { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan MaxSessionAge { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxSessionRefreshAttempts { get; init; } = 3;

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } =
        (duration, ct) => Task.Delay(duration, ct);

    public static MetaAdsOptions Default { get; } = new();

    public static MetaAdsOptions NoWaits { get; } = new()
    {
        Delay = (_, _) => Task.CompletedTask,
    };
}
