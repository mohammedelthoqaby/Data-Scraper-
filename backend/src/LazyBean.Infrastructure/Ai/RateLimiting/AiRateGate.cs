using LazyBean.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai.RateLimiting;

/// <summary>
/// In-process token buckets per provider, one on requests and one on estimated tokens.
/// Calls wait for budget up to a cap and are skipped otherwise. Retry-After and
/// x-ratelimit-* headers block or resize the buckets, and the refill rate backs off
/// after 429 responses and recovers gradually on success. Singleton: state is shared by all scopes.
/// </summary>
public sealed class AiRateGate
{
    private const double MinFactor = 0.25;
    private const double RecoveryStep = 0.05;

    private readonly AiOptions _options;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public AiRateGate(IOptions<AiOptions> options, TimeProvider? time = null)
    {
        _options = options.Value;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True while the provider is blocked (Retry-After or exhausted reset window).</summary>
    public bool IsBlocked(string provider, out TimeSpan remaining)
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            var bucket = GetBucket(provider, now);
            if (bucket.BlockedUntil > now)
            {
                remaining = bucket.BlockedUntil - now;
                return true;
            }

            remaining = TimeSpan.Zero;
            return false;
        }
    }

    /// <summary>
    /// Takes one request and <paramref name="estimatedTokens"/> from the buckets, waiting if needed.
    /// Returns false when the budget cannot be obtained within <paramref name="maxWait"/>.
    /// </summary>
    public async Task<bool> TryAcquireAsync(string provider, int estimatedTokens, TimeSpan maxWait, CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + maxWait;

        while (true)
        {
            TimeSpan wait;
            lock (_sync)
            {
                var now = _time.GetUtcNow();
                var bucket = GetBucket(provider, now);

                if (bucket.BlockedUntil > now)
                {
                    wait = bucket.BlockedUntil - now;
                }
                else
                {
                    var tokens = Math.Min(Math.Max(1, estimatedTokens), bucket.TokenCapacity);
                    var needRequests = 1 - bucket.Requests;
                    var needTokens = tokens - bucket.Tokens;

                    if (needRequests <= 0 && needTokens <= 0)
                    {
                        bucket.Requests -= 1;
                        bucket.Tokens -= tokens;
                        return true;
                    }

                    var requestWait = needRequests > 0 ? needRequests / bucket.EffectiveRequestsPerSecond : 0;
                    var tokenWait = needTokens > 0 ? needTokens / bucket.EffectiveTokensPerSecond : 0;
                    wait = TimeSpan.FromSeconds(Math.Max(requestWait, tokenWait));
                }
            }

            var budget = deadline - _time.GetUtcNow();
            if (wait > budget)
                return false;

            await Task.Delay(wait, _time, ct);
        }
    }

    /// <summary>Returns the difference between the estimate and the real usage to the token bucket.</summary>
    public void Settle(string provider, int estimatedTokens, int actualTokens)
    {
        lock (_sync)
        {
            var bucket = GetBucket(provider, _time.GetUtcNow());
            var refund = Math.Max(0, estimatedTokens) - Math.Max(0, actualTokens);
            bucket.Tokens = Math.Min(bucket.TokenCapacity, bucket.Tokens + refund);
        }
    }

    public void Block(string provider, TimeSpan duration)
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            var bucket = GetBucket(provider, now);
            var until = now + duration;
            if (until > bucket.BlockedUntil)
                bucket.BlockedUntil = until;
        }
    }

    /// <summary>Applies x-ratelimit-* headers: exhausted windows block until reset, remaining tokens cap the bucket.</summary>
    public void ObserveRateLimit(string provider, AiRateLimitInfo? info)
    {
        if (info is null)
            return;

        lock (_sync)
        {
            var now = _time.GetUtcNow();
            var bucket = GetBucket(provider, now);

            if (info.RemainingRequests == 0 && info.ResetRequests is { } resetRequests)
                Extend(bucket, now + resetRequests);

            if (info.RemainingTokens is { } remainingTokens)
            {
                bucket.Tokens = Math.Min(bucket.Tokens, Math.Max(0, remainingTokens));
                if (remainingTokens == 0 && info.ResetTokens is { } resetTokens)
                    Extend(bucket, now + resetTokens);
            }
        }
    }

    /// <summary>Lowers the refill rate after a 429 so the next calls are spaced out.</summary>
    public void ReportRateLimited(string provider)
    {
        lock (_sync)
        {
            var bucket = GetBucket(provider, _time.GetUtcNow());
            bucket.Factor = Math.Max(MinFactor, bucket.Factor * 0.5);
        }
    }

    /// <summary>Recovers the refill rate gradually after successful calls.</summary>
    public void ReportSuccess(string provider)
    {
        lock (_sync)
        {
            var bucket = GetBucket(provider, _time.GetUtcNow());
            bucket.Factor = Math.Min(1.0, bucket.Factor + RecoveryStep);
        }
    }

    private static void Extend(Bucket bucket, DateTimeOffset until)
    {
        if (until > bucket.BlockedUntil)
            bucket.BlockedUntil = until;
    }

    private Bucket GetBucket(string provider, DateTimeOffset now)
    {
        if (_buckets.TryGetValue(provider, out var existing))
        {
            Refill(existing, now);
            return existing;
        }

        var limits = _options.ForProvider(provider);
        var bucket = new Bucket(
            requestCapacity: Math.Max(1, limits.RequestsPerMinute),
            tokenCapacity: Math.Max(1, limits.TokensPerMinute),
            now: now);

        _buckets[provider] = bucket;
        return bucket;
    }

    private static void Refill(Bucket bucket, DateTimeOffset now)
    {
        var elapsed = (now - bucket.LastRefill).TotalSeconds;
        if (elapsed <= 0)
            return;

        bucket.Requests = Math.Min(bucket.RequestCapacity, bucket.Requests + elapsed * bucket.EffectiveRequestsPerSecond);
        bucket.Tokens = Math.Min(bucket.TokenCapacity, bucket.Tokens + elapsed * bucket.EffectiveTokensPerSecond);
        bucket.LastRefill = now;
    }

    private sealed class Bucket(double requestCapacity, double tokenCapacity, DateTimeOffset now)
    {
        public double RequestCapacity { get; } = requestCapacity;
        public double TokenCapacity { get; } = tokenCapacity;
        public double Requests { get; set; } = requestCapacity;
        public double Tokens { get; set; } = tokenCapacity;
        public DateTimeOffset LastRefill { get; set; } = now;
        public DateTimeOffset BlockedUntil { get; set; } = DateTimeOffset.MinValue;
        public double Factor { get; set; } = 1.0;

        public double EffectiveRequestsPerSecond => RequestCapacity / 60.0 * Factor;
        public double EffectiveTokensPerSecond => TokenCapacity / 60.0 * Factor;
    }
}
