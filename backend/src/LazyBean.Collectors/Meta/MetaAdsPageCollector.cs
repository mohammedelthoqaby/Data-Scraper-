namespace LazyBean.Collectors.Meta;

// C# port of the Python collect.py flow for one Facebook page ID: a fresh session per page (the
// Python script also builds a new client per call), the PAGE search with status ALL and
// impressions sort, cursor pagination with the same delays and retry rules, and the
// Success / Blocked / Failed mapping that the ingest endpoint records per page.
public sealed class MetaAdsPageCollector(MetaAdsOptions? options = null, HttpMessageHandler? handler = null)
{
    private const int PageSize = 10;
    private const string AuthenticationErrorType = "AuthenticationError";
    private const string SessionExpiredErrorType = "SessionExpiredError";

    private static readonly string[] BlockedKeywords = ["rate", "limit", "block", "challenge", "checkpoint", "captcha"];

    private readonly MetaAdsOptions _options = options ?? MetaAdsOptions.Default;

    public async Task<MetaPageResult> CollectPageAsync(
        string pageId,
        string country = "EG",
        string status = "ALL",
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var countryCode = country.ToUpperInvariant();

        using var session = new MetaAdsSession(_options, handler);
        try
        {
            var ads = await SearchPageAsync(session, pageId, countryCode, status.ToUpperInvariant(), maxResults, ct);
            return new MetaPageResult(pageId, countryCode, "Success", ads, null, null);
        }
        catch (MetaRateLimitException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Rate limit encountered from Meta: {ex.Message}", "RateLimitError");
        }
        catch (MetaAuthenticationException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Session or verification challenge from Meta: {ex.Message}", AuthenticationErrorType);
        }
        catch (MetaSessionExpiredException ex)
        {
            return Failure(pageId, countryCode, "Blocked", $"Session or verification challenge from Meta: {ex.Message}", SessionExpiredErrorType);
        }
        catch (MetaAdsException ex)
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
        MetaAdsSession session,
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
        var maxRetries = _options.SearchMaxRetries;

        while (ads.Count < maxResults)
        {
            var retryCount = 0;
            MetaSearchResponse? response = null;
            string? responseError = null;

            while (retryCount < maxRetries)
            {
                try
                {
                    response = await session.SearchAdsAsync(
                        new MetaSearchRequest(
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
                            await _options.Delay(
                                _options.RateLimitRetryBase * retryCount + RandomSeconds(1, 3),
                                ct);
                            continue;
                        }

                        throw new MetaRateLimitException("Max retries exceeded due to rate limiting");
                    }

                    if (response.SessionExpired)
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            await _options.Delay(_options.SessionRetryDelay, ct);
                            continue;
                        }

                        throw new MetaSessionExpiredException("Max retries exceeded due to session expiry");
                    }

                    responseError = response.Error;
                    break;
                }
                catch (MetaAdsException)
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

                    await _options.Delay(_options.TransportRetryBase * retryCount, ct);
                }
            }

            if (responseError is not null)
            {
                throw new MetaAdsException(responseError);
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
                    throw new MetaAdsException("Exceeded 25 consecutive empty pages with pagination cursors");
                }
            }
            else
            {
                consecutiveEmptyPages = 0;
            }

            foreach (var raw in response.Ads)
            {
                if (ads.Count >= maxResults)
                {
                    break;
                }

                ads.Add(MetaAdParser.Parse(raw));
            }

            if (response.NextCursor is null || ads.Count >= maxResults)
            {
                break;
            }

            if (!seenCursors.Add(response.NextCursor))
            {
                throw new MetaAdsException($"Repeated pagination cursor detected: '{response.NextCursor}'");
            }

            cursor = response.NextCursor;
            await _options.Delay(_options.PageDelay + RandomSeconds(0, _options.PageJitter.TotalSeconds), ct);
        }

        return ads;
    }

    private static MetaPageResult Failure(string pageId, string country, string status, string error, string errorType) =>
        new(pageId, country, status, [], error, errorType);

    private static TimeSpan RandomSeconds(double min, double max) =>
        TimeSpan.FromSeconds(min + Random.Shared.NextDouble() * (max - min));
}
