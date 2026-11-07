using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LazyBean.Collectors.Meta;

// C# port of meta_ads_collector.client.MetaAdsClient. Bootstraps tokens from the logged-out
// facebook.com homepage (the Ad Library page itself answers non-browser clients with a JS
// challenge), keeps cookies in a CookieContainer, and posts the AdLibrarySearchPaginationQuery
// GraphQL document the same way the Python client does. Headers mirror the Python library's
// Chrome 145 fingerprint; there is no TLS impersonation, so live behaviour must be verified.
public sealed class MetaAdsSession : IDisposable
{
    public const string HomeUrl = "https://www.facebook.com/";
    public const string AdLibraryUrl = "https://www.facebook.com/ads/library/";
    public const string GraphqlUrl = "https://www.facebook.com/api/graphql/";
    public const string SearchDocId = "25464068859919530";
    public const string SearchFriendlyName = "AdLibrarySearchPaginationQuery";
    public const string FallbackRev = "1033837939";

    private const string ChromeVersion = "145";
    private const string ChromeFullVersion = "145.0.7632.110";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36";
    private const string FallbackHs = "20476.HYP:comet_plat_default_pkg.2.1...0";
    private const string DefaultAsbdId = "359341";
    private const string DefaultVersion = "fbece7";
    private const string ViewportWidth = "1920";
    private const string ViewportHeight = "1080";
    private const string Dpr = "1.25";
    private const string SortByImpressions = "SORT_BY_TOTAL_IMPRESSIONS";

    private static readonly string[] GraphqlTokensWhenPresent = ["__dyn", "__csr", "fb_dtsg"];

    private readonly MetaAdsOptions _options;
    private readonly HttpMessageHandler? _handlerOverride;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private HttpClient _http = null!;
    private CookieContainer _cookies = null!;
    private int _requestCounter;
    private bool _initialized;
    private DateTimeOffset? _initTime;
    private int _consecutiveErrors;
    private int _consecutiveRefreshFailures;
    private bool _disposed;

    // handler is a test seam: when supplied, cookies are not wired and the handler is reused on refresh.
    public MetaAdsSession(MetaAdsOptions? options = null, HttpMessageHandler? handler = null)
    {
        _options = options ?? MetaAdsOptions.Default;
        _handlerOverride = handler;
        CreateHttpClient();
    }

    public MetaAdsOptions Options => _options;

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            SeedViewportCookies();

            var navigation = NavigationHeaders();
            navigation["sec-fetch-site"] = "none";

            using var response = await SendWithRetriesAsync(HttpMethod.Get, HomeUrl, navigation, null, ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new MetaAuthenticationException(
                    $"Homepage bootstrap failed (HTTP {(int)response.StatusCode}) -- could not obtain tokens from facebook.com");
            }

            var html = await response.Content.ReadAsStringAsync(ct);

            _tokens.Clear();
            foreach (var (name, value) in MetaTokens.ExtractTokens(html))
            {
                _tokens[name] = value;
            }

            _tokens.TryAdd("__spin_t", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            _tokens.TryAdd("__spin_b", "trunk");

            if (!_tokens.ContainsKey("__rev"))
            {
                var revision = MetaTokens.ServerRevision.Match(html);
                _tokens["__rev"] = revision.Success ? revision.Groups[1].Value : FallbackRev;
            }

            VerifyTokens();

            _initialized = true;
            _initTime = DateTimeOffset.UtcNow;
            _consecutiveErrors = 0;

            await _options.Delay(RandomBetween(_options.BootstrapDelayMin, _options.BootstrapDelayMax), ct);
        }
        catch (MetaAdsException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MetaAuthenticationException($"Failed to initialize client: {ex.Message}");
        }
    }

    // search_ads: one AdLibrarySearchPaginationQuery page. Returns flattened ad objects plus the next cursor.
    public async Task<MetaSearchResponse> SearchAdsAsync(MetaSearchRequest request, CancellationToken ct)
    {
        if (!_initialized)
        {
            await InitializeAsync(ct);
        }

        if (IsSessionStale() && !await RefreshSessionAsync(ct))
        {
            throw new MetaSessionExpiredException("Failed to refresh stale session");
        }

        var variables = BuildVariables(request);
        var payload = BuildGraphqlPayload(SearchDocId, variables, SearchFriendlyName);

        var headers = GraphqlHeaders();
        headers["x-fb-friendly-name"] = SearchFriendlyName;
        headers["x-fb-lsd"] = _tokens.GetValueOrDefault("lsd", string.Empty);
        if (_tokens.TryGetValue("x-asbd-id", out var asbd))
        {
            headers["x-asbd-id"] = asbd;
        }

        headers["referer"] =
            $"{AdLibraryUrl}?active_status={request.ActiveStatus.ToLowerInvariant()}" +
            $"&ad_type={AdTypeUrlParameter(request.AdType)}&country={request.Country}" +
            $"&q={Uri.EscapeDataString(request.Query)}";

        using var response = await PostGraphqlAsync(payload, headers, ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new MetaAdsException($"GraphQL request failed with status {(int)response.StatusCode}");
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.StartsWith("for (;;);", StringComparison.Ordinal))
        {
            text = text["for (;;);".Length..];
        }

        using var document = JsonDocument.Parse(text);
        return await HandleSearchDocumentAsync(document.RootElement, ct);
    }

    private void CreateHttpClient()
    {
        _cookies = new CookieContainer();

        HttpMessageHandler handler = _handlerOverride ?? new SocketsHttpHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        var previous = _http;
        _http = new HttpClient(handler, disposeHandler: _handlerOverride is null)
        {
            Timeout = _options.RequestTimeout,
        };
        previous?.Dispose();
    }

    private void SeedViewportCookies()
    {
        _cookies.Add(new Cookie("wd", $"{ViewportWidth}x{ViewportHeight}", "/", ".facebook.com"));
        _cookies.Add(new Cookie("dpr", Dpr, "/", ".facebook.com"));
    }

    private void VerifyTokens()
    {
        if (!_tokens.TryGetValue("lsd", out var lsd) || string.IsNullOrEmpty(lsd))
        {
            throw new MetaAuthenticationException(
                "Could not extract a real LSD token from the facebook.com homepage -- session bootstrap failed.  " +
                "Facebook may have changed its page structure.");
        }

        _tokens.TryAdd("jazoest", MetaTokens.CalculateJazoest(lsd));
        _tokens.TryAdd("__hsi", UnixMilliseconds());
        _tokens.TryAdd("__hs", FallbackHs);
        _tokens.TryAdd("__comet_req", "94");
        _tokens.TryAdd("v", DefaultVersion);
        _tokens.TryAdd("x-asbd-id", DefaultAsbdId);
    }

    private async Task<bool> RefreshSessionAsync(CancellationToken ct)
    {
        if (_consecutiveRefreshFailures >= _options.MaxSessionRefreshAttempts)
        {
            throw new MetaSessionExpiredException(
                $"Session refresh failed {_consecutiveRefreshFailures} consecutive times " +
                $"(max {_options.MaxSessionRefreshAttempts}). The Ad Library may be blocking this client.");
        }

        CreateHttpClient();
        _tokens.Clear();
        _initialized = false;
        _requestCounter = 0;
        _consecutiveErrors = 0;

        try
        {
            await InitializeAsync(ct);
            _consecutiveRefreshFailures = 0;
            return true;
        }
        catch (MetaAdsException)
        {
            _consecutiveRefreshFailures++;
            return false;
        }
    }

    private bool IsSessionStale() =>
        _initTime is null || DateTimeOffset.UtcNow - _initTime.Value > _options.MaxSessionAge;

    // _make_request: retries 429 and transport failures with exponential backoff and merges the
    // session's default navigation headers with the per-request headers, as the Python client does.
    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        Func<HttpContent>? contentFactory,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < _options.HttpMaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (contentFactory is not null)
                {
                    request.Content = contentFactory();
                }

                foreach (var (name, value) in MergeHeaders(headers))
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    response.Dispose();
                    await _options.Delay(BackoffDelay(attempt), ct);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastException = ex;
            }

            if (attempt < _options.HttpMaxRetries - 1)
            {
                await _options.Delay(BackoffDelay(attempt), ct);
            }
        }

        throw new MetaTransportException("Request failed after all retries", lastException);
    }

    private async Task<HttpResponseMessage> PostGraphqlAsync(
        Dictionary<string, string> payload,
        Dictionary<string, string> headers,
        CancellationToken ct)
    {
        var response = await SendWithRetriesAsync(HttpMethod.Post, GraphqlUrl, headers, () => new FormUrlEncodedContent(payload), ct);
        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return response;
        }

        // A 403 usually means expired cookies or tokens: rebuild the lsd-dependent fields and retry once.
        if (!await RefreshSessionAsync(ct))
        {
            return response;
        }

        response.Dispose();

        var lsd = _tokens.GetValueOrDefault("lsd", string.Empty);
        payload["lsd"] = lsd;
        payload["jazoest"] = MetaTokens.CalculateJazoest(lsd);
        payload["__rev"] = _tokens.GetValueOrDefault("__rev", FallbackRev);
        payload["__spin_r"] = _tokens.GetValueOrDefault("__spin_r", FallbackRev);
        payload["__spin_t"] = _tokens.GetValueOrDefault("__spin_t", UnixSeconds());
        payload["__spin_b"] = _tokens.GetValueOrDefault("__spin_b", "trunk");
        payload["__hsi"] = _tokens.GetValueOrDefault("__hsi", UnixMilliseconds());
        foreach (var token in new[] { "__dyn", "__csr", "fb_dtsg", "__hsdp", "__hblp" })
        {
            if (_tokens.TryGetValue(token, out var value))
            {
                payload[token] = value;
            }
            else
            {
                payload.Remove(token);
            }
        }

        headers["x-fb-lsd"] = lsd;
        return await SendWithRetriesAsync(HttpMethod.Post, GraphqlUrl, headers, () => new FormUrlEncodedContent(payload), ct);
    }

    private Dictionary<string, string> BuildGraphqlPayload(string docId, JsonObject variables, string friendlyName)
    {
        _requestCounter++;

        var lsd = _tokens.GetValueOrDefault("lsd", string.Empty);
        var jazoest = _tokens.TryGetValue("jazoest", out var stored) && stored.Length > 0
            ? stored
            : MetaTokens.CalculateJazoest(lsd);

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["av"] = "0",
            ["__aaid"] = "0",
            ["__user"] = "0",
            ["__a"] = "1",
            ["__req"] = MetaTokens.EncodeRequestId(_requestCounter),
            ["__hs"] = _tokens.GetValueOrDefault("__hs", FallbackHs),
            ["dpr"] = "1",
            ["__ccg"] = "GOOD",
            ["__rev"] = _tokens.GetValueOrDefault("__rev", FallbackRev),
            ["__s"] = GenerateShortId(),
            ["__hsi"] = _tokens.GetValueOrDefault("__hsi", UnixMilliseconds()),
            ["__comet_req"] = _tokens.GetValueOrDefault("__comet_req", "94"),
            ["lsd"] = lsd,
            ["jazoest"] = jazoest,
            ["__spin_r"] = _tokens.GetValueOrDefault("__spin_r", FallbackRev),
            ["__spin_b"] = _tokens.GetValueOrDefault("__spin_b", "trunk"),
            ["__spin_t"] = _tokens.GetValueOrDefault("__spin_t", UnixSeconds()),
            ["__jssesw"] = "1",
            ["fb_api_caller_class"] = "RelayModern",
            ["fb_api_req_friendly_name"] = friendlyName,
            ["server_timestamps"] = "true",
            ["variables"] = variables.ToJsonString(),
            ["doc_id"] = docId,
        };

        // Only send tokens that were genuinely extracted: stale fallbacks trigger Meta error 1675004.
        foreach (var token in GraphqlTokensWhenPresent)
        {
            if (_tokens.TryGetValue(token, out var value))
            {
                payload[token] = value;
            }
        }

        if (_tokens.TryGetValue("__hsdp", out var hsdp))
        {
            payload["__hsdp"] = hsdp;
        }

        if (_tokens.TryGetValue("__hblp", out var hblp))
        {
            payload["__hblp"] = hblp;
        }

        return payload;
    }

    private static JsonObject BuildVariables(MetaSearchRequest request)
    {
        var variables = new JsonObject
        {
            ["activeStatus"] = request.ActiveStatus,
            ["adType"] = request.AdType,
            ["bylines"] = new JsonArray(),
            ["collationToken"] = request.CollationToken,
            ["contentLanguages"] = new JsonArray(),
            ["countries"] = ToJsonArray([request.Country]),
            ["excludedIDs"] = new JsonArray(),
            ["first"] = request.First,
            ["isTargetedCountry"] = false,
            ["location"] = null,
            ["mediaType"] = request.MediaType,
            ["multiCountryFilterMode"] = null,
            ["pageIDs"] = ToJsonArray(request.PageIds),
            ["potentialReachInput"] = new JsonArray(),
            ["publisherPlatforms"] = new JsonArray(),
            ["queryString"] = request.Query,
            ["regions"] = new JsonArray(),
            ["searchType"] = request.SearchType,
            ["sessionID"] = request.SessionId,
            ["source"] = null,
            ["startDate"] = null,
            ["v"] = DefaultVersion,
            ["viewAllPageID"] = "0",
        };

        // Only SORT_BY_TOTAL_IMPRESSIONS carries sortData; other sort strings fail server-side.
        if (request.SortMode == SortByImpressions)
        {
            variables["sortData"] = new JsonObject
            {
                ["direction"] = request.SortDirection,
                ["mode"] = request.SortMode,
            };
        }

        if (!string.IsNullOrEmpty(request.Cursor))
        {
            variables["cursor"] = request.Cursor;
        }

        return variables;
    }

    private async Task<MetaSearchResponse> HandleSearchDocumentAsync(JsonElement root, CancellationToken ct)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                var code = error.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                    ? codeEl.GetInt64()
                    : (long?)null;
                var message = error.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String
                    ? messageEl.GetString() ?? "Unknown error"
                    : "Unknown error";

                if (code == 1675004 || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    _consecutiveErrors++;
                    await _options.Delay(_options.GraphqlRateLimitWait + RandomBetween(TimeSpan.Zero, TimeSpan.FromSeconds(3)), ct);
                    return new MetaSearchResponse([], null, RateLimited: true, Error: message);
                }

                if (code is 1357004 or 1357001 || message.Contains("session", StringComparison.OrdinalIgnoreCase))
                {
                    _consecutiveErrors++;
                    if (_consecutiveErrors >= 2)
                    {
                        await RefreshSessionAsync(ct);
                    }

                    return new MetaSearchResponse([], null, SessionExpired: true, Error: message);
                }
            }

            var data = root.TryGetProperty("data", out var dataEl) ? dataEl : default;
            if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False)
            {
                return new MetaSearchResponse([], null, Error: errors.GetRawText());
            }
        }

        _consecutiveErrors = 0;
        _consecutiveRefreshFailures = 0;
        return ParseSearchResponse(root);
    }

    // _parse_search_response: data.ad_library_main.search_results_connection (or the camelCase
    // variant), edges[].node.collated_results[], with snapshot fields overlaid on each ad.
    private static MetaSearchResponse ParseSearchResponse(JsonElement root)
    {
        var data = Child(root, "data");
        var results = Child(Child(data, "ad_library_main"), "search_results_connection");
        if (IsEmptyObject(results))
        {
            results = Child(Child(data, "adLibraryMain"), "searchResultsConnection");
        }

        if (IsEmptyObject(results))
        {
            results = data;
        }

        var pageInfo = Child(results, "page_info");
        if (IsEmptyObject(pageInfo))
        {
            pageInfo = Child(results, "pageInfo");
        }

        string? nextCursor = null;
        if (IsTruthy(pageInfo, "has_next_page") || IsTruthy(pageInfo, "hasNextPage"))
        {
            nextCursor = ReadString(pageInfo, "end_cursor") ?? ReadString(pageInfo, "endCursor");
        }

        var ads = new List<JsonObject>();
        foreach (var edge in ChildArray(results, "edges"))
        {
            var node = Child(edge, "node");
            if (node.ValueKind != JsonValueKind.Object)
            {
                node = edge;
            }

            foreach (var adData in ChildArray(node, "collated_results"))
            {
                if (adData.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var flattened = JsonNode.Parse(adData.GetRawText())!.AsObject();
                var snapshot = Child(adData, "snapshot");
                if (snapshot.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in snapshot.EnumerateObject())
                    {
                        if (!flattened.ContainsKey(property.Name))
                        {
                            flattened[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                        }
                    }
                }

                ads.Add(flattened);
            }
        }

        return new MetaSearchResponse(ads, nextCursor);
    }

    private Dictionary<string, string> GraphqlHeaders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["accept"] = "*/*",
        ["accept-language"] = "en-US,en;q=0.9",
        ["origin"] = "https://www.facebook.com",
        ["sec-ch-prefers-color-scheme"] = "light",
        ["sec-ch-ua"] = $"\"Google Chrome\";v=\"{ChromeVersion}\", \"Chromium\";v=\"{ChromeVersion}\", \"Not_A Brand\";v=\"24\"",
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["sec-ch-ua-platform-version"] = "\"15.0.0\"",
        ["sec-fetch-dest"] = "empty",
        ["sec-fetch-mode"] = "cors",
        ["sec-fetch-site"] = "same-origin",
        ["user-agent"] = UserAgent,
    };

    private static Dictionary<string, string> NavigationHeaders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
        ["accept-language"] = "en-US,en;q=0.9",
        ["cache-control"] = "max-age=0",
        ["dpr"] = Dpr,
        ["sec-ch-prefers-color-scheme"] = "light",
        ["sec-ch-ua"] = $"\"Google Chrome\";v=\"{ChromeVersion}\", \"Chromium\";v=\"{ChromeVersion}\", \"Not_A Brand\";v=\"24\"",
        ["sec-ch-ua-full-version-list"] = $"\"Google Chrome\";v=\"{ChromeFullVersion}\", \"Chromium\";v=\"{ChromeFullVersion}\", \"Not_A Brand\";v=\"24.0.0.0\"",
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-model"] = "\"\"",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["sec-ch-ua-platform-version"] = "\"15.0.0\"",
        ["sec-fetch-dest"] = "document",
        ["sec-fetch-mode"] = "navigate",
        ["sec-fetch-site"] = "none",
        ["sec-fetch-user"] = "?1",
        ["upgrade-insecure-requests"] = "1",
        ["user-agent"] = UserAgent,
        ["viewport-width"] = ViewportWidth,
    };

    private static Dictionary<string, string> MergeHeaders(IReadOnlyDictionary<string, string> perRequest)
    {
        var merged = NavigationHeaders();
        foreach (var (name, value) in perRequest)
        {
            merged[name] = value;
        }

        return merged;
    }

    private static string AdTypeUrlParameter(string adType) => adType switch
    {
        "POLITICAL_AND_ISSUE_ADS" => "political_and_issue_ads",
        "HOUSING_ADS" => "housing",
        "EMPLOYMENT_ADS" => "employment",
        "CREDIT_ADS" => "credit",
        _ => "all",
    };

    private static string GenerateShortId()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var parts = new string[3];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
        }

        return string.Join(':', parts);
    }

    private static string UnixMilliseconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private static string UnixSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static TimeSpan BackoffDelay(int attempt) =>
        TimeSpan.FromSeconds(2 * Math.Pow(2, attempt)) + TimeSpan.FromSeconds(Random.Shared.NextDouble());

    private static TimeSpan RandomBetween(TimeSpan min, TimeSpan max)
    {
        var spread = max - min;
        return min + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * spread.TotalMilliseconds);
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    private static IEnumerable<JsonElement> ChildArray(JsonElement element, string name)
    {
        var child = Child(element, name);
        return child.ValueKind == JsonValueKind.Array ? child.EnumerateArray() : [];
    }

    private static bool IsEmptyObject(JsonElement element) =>
        element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Any();

    private static bool IsTruthy(JsonElement element, string name)
    {
        var value = Child(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
            JsonValueKind.Number => value.GetDouble() != 0,
            _ => false,
        };
    }

    private static string? ReadString(JsonElement element, string name)
    {
        var value = Child(element, name);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())
            ? value.GetString()
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}

public sealed record MetaSearchRequest(
    string Country,
    string ActiveStatus,
    IReadOnlyList<string> PageIds,
    int First,
    string SessionId,
    string CollationToken,
    string? Cursor = null,
    string SearchType = "PAGE",
    string Query = "",
    string AdType = "ALL",
    string MediaType = "ALL",
    string? SortMode = "SORT_BY_TOTAL_IMPRESSIONS",
    string SortDirection = "DESCENDING");

public sealed record MetaSearchResponse(
    IReadOnlyList<JsonObject> Ads,
    string? NextCursor,
    bool RateLimited = false,
    bool SessionExpired = false,
    string? Error = null);
