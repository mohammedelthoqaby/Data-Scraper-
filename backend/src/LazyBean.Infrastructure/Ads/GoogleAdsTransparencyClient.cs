using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

public partial class GoogleAdsTransparencyClient(
    HttpClient httpClient,
    ILogger<GoogleAdsTransparencyClient> logger)
    : IGoogleAdsTransparencyClient
{
    private const string BaseUrl = "https://adstransparency.google.com";
    private const int RegionEgypt = 2818;

    [GeneratedRegex(@"<img[^>]+src=[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSrcRegex();

    [GeneratedRegex(@"https://(?:tpc\.googlesyndication\.com|lh\d+\.googleusercontent\.com)/[^""'\\\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex GoogleImageUrlRegex();

    public async Task<string?> SearchAdvertiserIdAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalizedQuery = query.Trim();
        if (normalizedQuery.StartsWith("AR", StringComparison.OrdinalIgnoreCase) && normalizedQuery.Length >= 10)
        {
            return normalizedQuery;
        }

        var rpcUrl = $"{BaseUrl}/anji/_/rpc/SearchService/SearchSuggestions?authuser=0";
        var payload = new Dictionary<string, object>
        {
            ["1"] = normalizedQuery,
            ["2"] = 10,
            ["3"] = 10
        };

        var formData = new Dictionary<string, string>
        {
            ["f.req"] = JsonSerializer.Serialize(payload)
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new FormUrlEncodedContent(formData)
        };
        ApplyStandardHeaders(req);

        HttpResponseMessage resp;
        try
        {
            resp = await httpClient.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach Google Ads SearchSuggestions endpoint for query '{Query}'.", normalizedQuery);
            throw;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        EnsureNotBlocked(resp.StatusCode, body, resp.Headers.Location?.ToString());

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Ads SearchSuggestions returned HTTP {StatusCode} for query '{Query}'.", (int)resp.StatusCode, normalizedQuery);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("1", out var suggestionsArray) || suggestionsArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? firstCandidateId = null;

            foreach (var suggestion in suggestionsArray.EnumerateArray())
            {
                if (suggestion.TryGetProperty("1", out var advInfo) && advInfo.ValueKind == JsonValueKind.Object)
                {
                    var advName = advInfo.TryGetProperty("1", out var np) ? np.GetString() : null;
                    var advId = advInfo.TryGetProperty("2", out var ip) ? ip.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(advId) && advId.StartsWith("AR", StringComparison.OrdinalIgnoreCase))
                    {
                        if (firstCandidateId == null)
                        {
                            firstCandidateId = advId;
                        }

                        if (!string.IsNullOrWhiteSpace(advName) &&
                            (string.Equals(advName, normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                             normalizedQuery.Contains(advName, StringComparison.OrdinalIgnoreCase) ||
                             advName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
                        {
                            return advId;
                        }
                    }
                }
            }

            return firstCandidateId;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Google Ads SearchSuggestions JSON response for query '{Query}'.", normalizedQuery);
            return null;
        }
    }

    public async Task<IReadOnlyList<GoogleAdvertiserCandidateDto>> SearchCandidatesAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GoogleAdvertiserCandidateDto>();
        }

        var normalizedQuery = query.Trim();
        if (normalizedQuery.StartsWith("AR", StringComparison.OrdinalIgnoreCase) && normalizedQuery.Length >= 10)
        {
            return new List<GoogleAdvertiserCandidateDto>
            {
                new(normalizedQuery, normalizedQuery, null, null)
            };
        }

        var rpcUrl = $"{BaseUrl}/anji/_/rpc/SearchService/SearchSuggestions?authuser=0";
        var payload = new Dictionary<string, object>
        {
            ["1"] = normalizedQuery,
            ["2"] = 10,
            ["3"] = 10
        };

        var formData = new Dictionary<string, string>
        {
            ["f.req"] = JsonSerializer.Serialize(payload)
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new FormUrlEncodedContent(formData)
        };
        ApplyStandardHeaders(req);

        HttpResponseMessage resp;
        try
        {
            resp = await httpClient.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach Google Ads SearchSuggestions endpoint for query '{Query}'.", normalizedQuery);
            throw;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        EnsureNotBlocked(resp.StatusCode, body, resp.Headers.Location?.ToString());

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Ads SearchSuggestions returned HTTP {StatusCode} for query '{Query}'.", (int)resp.StatusCode, normalizedQuery);
            return Array.Empty<GoogleAdvertiserCandidateDto>();
        }

        var candidates = new List<GoogleAdvertiserCandidateDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("1", out var suggestionsArray) || suggestionsArray.ValueKind != JsonValueKind.Array)
            {
                return candidates;
            }

            foreach (var suggestion in suggestionsArray.EnumerateArray())
            {
                if (suggestion.TryGetProperty("1", out var advInfo) && advInfo.ValueKind == JsonValueKind.Object)
                {
                    var advName = advInfo.TryGetProperty("1", out var np) && np.ValueKind == JsonValueKind.String ? np.GetString() ?? "" : "";
                    var advId = advInfo.TryGetProperty("2", out var ip) && ip.ValueKind == JsonValueKind.String ? ip.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(advId) &&
                        advId.StartsWith("AR", StringComparison.OrdinalIgnoreCase) &&
                        seenIds.Add(advId))
                    {
                        var region = advInfo.TryGetProperty("3", out var rp) && rp.ValueKind == JsonValueKind.String ? rp.GetString() : null;
                        int? approxCount = null;

                        if (advInfo.TryGetProperty("4", out var p4) &&
                            p4.TryGetProperty("2", out var p42) &&
                            p42.TryGetProperty("2", out var p422) &&
                            p422.TryGetInt32(out var count))
                        {
                            approxCount = count;
                        }

                        candidates.Add(new GoogleAdvertiserCandidateDto(advId, advName, region, approxCount));
                    }
                }
            }

            return candidates;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Google Ads SearchSuggestions JSON response for query '{Query}'.", normalizedQuery);
            return candidates;
        }
    }

    public async Task<GoogleCreativesPageResult> SearchCreativesAsync(string advertiserId, string? pageToken = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(advertiserId))
        {
            throw new ArgumentException("Advertiser ID must not be null or empty.", nameof(advertiserId));
        }

        var rpcUrl = $"{BaseUrl}/anji/_/rpc/SearchService/SearchCreatives";
        var payload = new Dictionary<string, object>
        {
            ["2"] = 40,
            ["3"] = new Dictionary<string, object>
            {
                ["12"] = new Dictionary<string, object> { ["1"] = "", ["2"] = true },
                ["13"] = new Dictionary<string, object> { ["1"] = new[] { advertiserId } },
                ["8"] = new[] { RegionEgypt }
            },
            ["7"] = new Dictionary<string, object> { ["1"] = 1 }
        };

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            payload["4"] = pageToken;
        }

        var formData = new Dictionary<string, string>
        {
            ["f.req"] = JsonSerializer.Serialize(payload)
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new FormUrlEncodedContent(formData)
        };
        ApplyStandardHeaders(req);

        HttpResponseMessage resp;
        try
        {
            resp = await httpClient.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach Google Ads SearchCreatives endpoint for advertiser '{AdvertiserId}'.", advertiserId);
            throw;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        EnsureNotBlocked(resp.StatusCode, body, resp.Headers.Location?.ToString());

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Ads SearchCreatives returned HTTP {StatusCode} for advertiser '{AdvertiserId}'.", (int)resp.StatusCode, advertiserId);
            return new GoogleCreativesPageResult([], null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var creatives = new List<GoogleCreativeDto>();

            if (root.TryGetProperty("1", out var adsArray) && adsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var adItem in adsArray.EnumerateArray())
                {
                    if (!adItem.TryGetProperty("2", out var idProp) || string.IsNullOrWhiteSpace(idProp.GetString()))
                    {
                        continue;
                    }

                    var creativeId = idProp.GetString()!;
                    var advId = adItem.TryGetProperty("1", out var aProp) && !string.IsNullOrWhiteSpace(aProp.GetString())
                        ? aProp.GetString()!
                        : advertiserId;

                    var advName = adItem.TryGetProperty("12", out var nProp) ? nProp.GetString() : null;

                    var formatId = adItem.TryGetProperty("4", out var fProp) && fProp.TryGetInt32(out var fid) ? fid : 0;
                    var formatName = formatId switch
                    {
                        1 => "text",
                        2 => "image",
                        3 => "video",
                        _ => "display"
                    };

                    DateTimeOffset? firstSeen = null;
                    if (adItem.TryGetProperty("6", out var fsObj) && fsObj.TryGetProperty("1", out var fsVal))
                    {
                        if (long.TryParse(fsVal.GetString(), out var fsSec) && fsSec > 0)
                        {
                            firstSeen = DateTimeOffset.FromUnixTimeSeconds(fsSec);
                        }
                    }

                    DateTimeOffset? lastSeen = null;
                    if (adItem.TryGetProperty("7", out var lsObj) && lsObj.TryGetProperty("1", out var lsVal))
                    {
                        if (long.TryParse(lsVal.GetString(), out var lsSec) && lsSec > 0)
                        {
                            lastSeen = DateTimeOffset.FromUnixTimeSeconds(lsSec);
                        }
                    }

                    var imageUrl = ExtractImageUrl(adItem);

                    var snapshotUrl = $"{BaseUrl}/advertiser/{advId}/creative/{creativeId}?region=EG";
                    var title = advName ?? "Google Ad";
                    string? bodyCopy = null;

                    creatives.Add(new GoogleCreativeDto(
                        creativeId,
                        advId,
                        advName,
                        formatId,
                        formatName,
                        title,
                        bodyCopy,
                        null,
                        snapshotUrl,
                        imageUrl,
                        firstSeen,
                        lastSeen,
                        adItem.GetRawText()));
                }
            }

            string? nextPageToken = null;
            if (root.TryGetProperty("2", out var nextProp) && nextProp.ValueKind == JsonValueKind.String)
            {
                nextPageToken = nextProp.GetString();
            }

            return new GoogleCreativesPageResult(creatives, nextPageToken);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Google Ads SearchCreatives JSON response for advertiser '{AdvertiserId}'.", advertiserId);
            return new GoogleCreativesPageResult([], null);
        }
    }

    private static string? ExtractImageUrl(JsonElement adItem)
    {
        if (!adItem.TryGetProperty("3", out var mediaObj))
        {
            return null;
        }

        // 1. Try finding HTML snippet in mediaObj -> "3" -> "2" or mediaObj -> "2"
        if (mediaObj.TryGetProperty("3", out var m3) && m3.TryGetProperty("2", out var snippet32) && snippet32.ValueKind == JsonValueKind.String)
        {
            var snippet = snippet32.GetString();
            if (!string.IsNullOrEmpty(snippet))
            {
                var match = ImageSrcRegex().Match(snippet);
                if (match.Success)
                {
                    return WebUtility.HtmlDecode(match.Groups["src"].Value);
                }
            }
        }

        if (mediaObj.TryGetProperty("2", out var snippet2) && snippet2.ValueKind == JsonValueKind.String)
        {
            var snippet = snippet2.GetString();
            if (!string.IsNullOrEmpty(snippet))
            {
                var match = ImageSrcRegex().Match(snippet);
                if (match.Success)
                {
                    return WebUtility.HtmlDecode(match.Groups["src"].Value);
                }
            }
        }

        // 2. Fallback: Search for Google CDN image URL in raw JSON
        var rawJson = mediaObj.GetRawText();
        var cdnMatch = GoogleImageUrlRegex().Match(rawJson);
        if (cdnMatch.Success)
        {
            return cdnMatch.Value.Replace("\\/", "/");
        }

        return null;
    }

    private static void ApplyStandardHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        req.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/?region=EG");
        req.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\", \"Google Chrome\";v=\"131\"");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        req.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        req.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        req.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
    }

    private static void EnsureNotBlocked(HttpStatusCode statusCode, string body, string? location)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new GoogleAdsBlockedException("Google Ads Transparency rate limited requests (HTTP 429).");
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            throw new GoogleAdsBlockedException("Google Ads Transparency returned HTTP 403 Forbidden.");
        }

        if ((location != null && location.Contains("/sorry/", StringComparison.OrdinalIgnoreCase)) ||
            body.Contains("/sorry/", StringComparison.OrdinalIgnoreCase))
        {
            throw new GoogleAdsBlockedException("Google Ads Transparency redirected to /sorry/ CAPTCHA interstitial.");
        }

        if (body.Contains("recaptcha", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase))
        {
            throw new GoogleAdsBlockedException("Google Ads Transparency triggered interactive verification / CAPTCHA challenge.");
        }
    }
}
