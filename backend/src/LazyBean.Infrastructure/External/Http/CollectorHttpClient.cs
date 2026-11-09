using LazyBean.Application.Collector.Exceptions;
using System.Collections.Concurrent;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.External.Http;

// Every outbound collector request goes through here: per-competitor delay (politeness),
// robots.txt enforcement, and 429/503 retry honoring Retry-After - adapters never touch
// HttpClient directly. One instance is shared (singleton) so the per-competitor "last
// request time" state is meaningful across the whole process.
public class CollectorHttpClient(
    IHttpClientFactory httpClientFactory,
    RobotsTxtCache robotsTxtCache,
    IEvidenceStore evidenceStore,
    ILogger<CollectorHttpClient> logger) : ICollectorHttpClient
{
    private const int MaxRetries = 3;

    // Serializes requests per competitor so the MinDelayMs gap between them is honored;
    // one flight at a time per competitor also matches "each competitor fetches sequentially."
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRequestAt = new();

    public async Task<CollectedResponse> GetAsync(Competitor competitor, string url, CancellationToken ct)
    {
        if (!await robotsTxtCache.IsAllowedAsync(url, ct))
        {
            logger.LogWarning("Skipping {Url} - disallowed by robots.txt.", url);
            return new CollectedResponse(0, string.Empty, false, "Disallowed by robots.txt");
        }

        var gate = _gates.GetOrAdd(competitor.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await DelaySinceLastRequestAsync(competitor, ct);
            return await GetWithRetryAsync(competitor, url, ct);
        }
        finally
        {
            _lastRequestAt[competitor.Id] = DateTimeOffset.UtcNow;
            gate.Release();
        }
    }

    private async Task DelaySinceLastRequestAsync(Competitor competitor, CancellationToken ct)
    {
        if (!_lastRequestAt.TryGetValue(competitor.Id, out var last))
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - last;
        var minDelay = TimeSpan.FromMilliseconds(competitor.MinDelayMs);
        if (elapsed < minDelay)
        {
            await Task.Delay(minDelay - elapsed, ct);
        }
    }

    private async Task<CollectedResponse> GetWithRetryAsync(Competitor competitor, string url, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("Collector");

        int? lastStatus = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri(url), ct);
                lastStatus = (int)response.StatusCode;

                if (HttpRetryHelper.IsRetryableStatusCode((int)response.StatusCode))
                {
                    var delay = GetRetryAfterDelay(response) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    logger.LogWarning(
                        "{Url} returned {Status}; retrying in {Delay}s (attempt {Attempt}/{Max}).",
                        url, (int)response.StatusCode, delay.TotalSeconds, attempt, MaxRetries);
                    await Task.Delay(delay, ct);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct);

                if (CollectionRunContext.CurrentRunId is { } runId)
                {
                    await evidenceStore.SaveAsync(runId, competitor.Id, url, (int)response.StatusCode, body, ct);
                }

                var headers = response.Headers
                    .Concat(response.Content.Headers)
                    .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

                return new CollectedResponse((int)response.StatusCode, body, response.IsSuccessStatusCode, Headers: headers);
            }
            catch (Exception ex) when (HttpRetryHelper.IsRetryableException(ex, ct))
            {
                lastException = ex;
                logger.LogWarning(ex, "Attempt {Attempt}/{Max} fetching {Url} threw retryable error.", attempt, MaxRetries, url);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        throw new CollectionFetchException(url, lastStatus, lastException);
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
