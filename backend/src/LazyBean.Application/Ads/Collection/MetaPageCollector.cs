using LazyBean.Application.Ads.Models;
using LazyBean.Application.Ads.Ports;
using LazyBean.Application.Collector.Exceptions;

namespace LazyBean.Application.Ads.Collection;

// The collection of one Facebook page: cursor paging, the retry and wait rules, and the Success / Blocked / Failed outcome the
// ingest records for the page. The HTTP session and the reading of Meta's responses sit behind IMetaAdsSource.
public interface IMetaPageCollector
{
    Task<MetaPageResult> CollectPageAsync(string pageId, string country, string status, int maxResults, CancellationToken ct);
}

// The waits and retry counts of a page. The defaults are the values the Python collector used.
public sealed record MetaCollectionPolicy
{
    public int SearchMaxRetries { get; init; } = 3;
    public TimeSpan RateLimitRetryBase { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan SessionRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TransportRetryBase { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan PageDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PageJitter { get; init; } = TimeSpan.FromSeconds(1);

    public static MetaCollectionPolicy Default { get; } = new();
}

// C# port of the Python collect.py flow for one Facebook page ID: a fresh session per page, the PAGE search with status ALL
// and impressions sort, cursor pagination with the same delays and retry rules, and the Success / Blocked / Failed mapping.
public sealed class MetaPageCollector(
    IMetaAdsSource source,
    MetaCollectionPolicy? policy = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMetaPageCollector
{
    private const int PageSize = 10;
    private const string AuthenticationErrorType = "AuthenticationError";
    private const string SessionExpiredErrorType = "SessionExpiredError";

    private static readonly string[] BlockedKeywords = ["rate", "limit", "block", "challenge", "checkpoint", "captcha"];

    private readonly MetaCollectionPolicy _policy = policy ?? MetaCollectionPolicy.Default;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((d, ct) => Task.Delay(d, ct));

    public async Task<MetaPageResult> CollectPageAsync(
        string pageId,
        string country = "EG",
        string status = "ALL",
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var countryCode = country.ToUpperInvariant();

        using var session = source.OpenSession();
        try
        {
            var ads = await SearchPageAsync(session, pageId, countryCode, status.ToUpperInvariant(), maxResults, ct);
            return new MetaPageResult(pageId, countryCode, "Success", ads, null, null);
        }
        catch (CollectorRateLimitException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Rate limit encountered from Meta: {ex.Message}", "RateLimitError");
        }
        catch (CollectorAuthenticationException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Session or verification challenge from Meta: {ex.Message}", AuthenticationErrorType);
        }
        catch (CollectorSessionExpiredException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Session or verification challenge from Meta: {ex.Message}", SessionExpiredErrorType);
        }
        catch (CollectorSourceException ex)
        {
            var lower = ex.Message.ToLowerInvariant();
            var blocked = BlockedKeywords.Any(lower.Contains);
            return Failure(pageId, countryCode, blocked ? "Blocked" : "Failed", ex.Message, "MetaAdsError");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(pageId, countryCode, "Failed", $"Unexpected collection error: {ex.Message}", ex.GetType().Name);
        }
    }

    private async Task<List<MetaAdDto>> SearchPageAsync(
        IMetaSearchSession session,
        string pageId,
        string country,
        string status,
        int maxResults,
        CancellationToken ct)
    {
        var ads = new List<MetaAdDto>();
        var pageIds = new[] { pageId };
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var sessionId = Guid.NewGuid().ToString();
        var collationToken = Guid.NewGuid().ToString();
        string? cursor = null;
        var consecutiveEmptyPages = 0;
        var maxRetries = _policy.SearchMaxRetries;

        while (ads.Count < maxResults)
        {
            var retryCount = 0;
            MetaSearchStepResult? response = null;
            string? responseError = null;

            while (retryCount < maxRetries)
            {
                try
                {
                    response = await session.SearchAsync(
                        new MetaSearchStepRequest(
                            Country: country,
                            ActiveStatus: status,
                            PageIds: pageIds,
                            First: PageSize,
                            SessionId: sessionId,
                            CollationToken: collationToken,
                            Cursor: cursor,
                            SearchType: "PAGE"),
                        ct);

                    if (response.RateLimited)
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            await _delay(_policy.RateLimitRetryBase * retryCount + RandomSeconds(1, 3), ct);
                            continue;
                        }

                        throw new CollectorRateLimitException("Max retries exceeded due to rate limiting");
                    }

                    if (response.SessionExpired)
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            await _delay(_policy.SessionRetryDelay, ct);
                            continue;
                        }

                        throw new CollectorSessionExpiredException("Max retries exceeded due to session expiry");
                    }

                    responseError = response.Error;
                    break;
                }
                catch (CollectorSourceException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    response = null;
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw;
                    }

                    await _delay(_policy.TransportRetryBase * retryCount, ct);
                }
            }

            if (responseError is not null)
            {
                throw new CollectorSourceException(responseError);
            }

            if (response is null || (response.Ads.Count == 0 && response.NextCursor is null))
            {
                break;
            }

            if (response.Ads.Count == 0)
            {
                consecutiveEmptyPages++;
                if (consecutiveEmptyPages > 25)
                {
                    throw new CollectorSourceException("Exceeded 25 consecutive empty pages with pagination cursors");
                }
            }
            else
            {
                consecutiveEmptyPages = 0;
            }

            foreach (var ad in response.Ads)
            {
                if (ads.Count >= maxResults)
                {
                    break;
                }

                ads.Add(ad);
            }

            if (response.NextCursor is null || ads.Count >= maxResults)
            {
                break;
            }

            if (!seenCursors.Add(response.NextCursor))
            {
                throw new CollectorSourceException($"Repeated pagination cursor detected: '{response.NextCursor}'");
            }

            cursor = response.NextCursor;
            await _delay(_policy.PageDelay + RandomSeconds(0, _policy.PageJitter.TotalSeconds), ct);
        }

        return ads;
    }

    private static MetaPageResult Failure(string pageId, string country, string status, string error, string errorType) =>
        new(pageId, country, status, [], error, errorType);

    private static TimeSpan RandomSeconds(double min, double max) =>
        TimeSpan.FromSeconds(min + Random.Shared.NextDouble() * (max - min));
}
