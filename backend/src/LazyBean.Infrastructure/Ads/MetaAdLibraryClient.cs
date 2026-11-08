using System.Net.Http.Headers;
using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

public class MetaAdLibraryClient : IMetaAdLibraryClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<MetaAdLibraryClient> _logger;

    public MetaAdLibraryClient(HttpClient httpClient, IConfiguration config, ILogger<MetaAdLibraryClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    private string? GetToken()
    {
        return _config["Meta:AccessToken"];
    }

    public async Task<MetaTokenDebugInfo> DebugTokenAsync(CancellationToken ct = default)
    {
        var token = GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new MetaTokenDebugInfo(false, null, null, [], "Meta:AccessToken is not configured in user secrets.");
        }

        try
        {
            // Meta debug_token endpoint accepts input_token and access_token
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(token)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorEl))
            {
                var msg = errorEl.TryGetProperty("message", out var m) ? m.GetString() : "Unknown Meta error";
                return new MetaTokenDebugInfo(false, null, null, [], msg);
            }

            if (root.TryGetProperty("data", out var dataEl))
            {
                var isValid = dataEl.TryGetProperty("is_valid", out var iv) && iv.GetBoolean();
                DateTimeOffset? expiresAt = null;

                if (dataEl.TryGetProperty("expires_at", out var expEl) && expEl.TryGetInt64(out var expUnix) && expUnix > 0)
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                }

                string? appId = dataEl.TryGetProperty("app_id", out var aEl) ? aEl.GetString() : null;

                var scopes = new List<string>();
                if (dataEl.TryGetProperty("scopes", out var scopesEl) && scopesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in scopesEl.EnumerateArray())
                    {
                        var sStr = s.GetString();
                        if (sStr != null) scopes.Add(sStr);
                    }
                }

                return new MetaTokenDebugInfo(isValid, expiresAt, appId, scopes, null);
            }

            return new MetaTokenDebugInfo(false, null, null, [], "Unexpected response format from Meta debug_token.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect Meta access token health.");
            return new MetaTokenDebugInfo(false, null, null, [], ex.Message);
        }
    }

    public async Task<IReadOnlyList<MetaAdRawItem>> FetchAdsAsync(string pageId, CancellationToken ct = default)
    {
        var token = GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Meta:AccessToken is not configured. Cannot fetch ads without credentials.");
        }

        var results = new List<MetaAdRawItem>();
        var fields = "id,ad_creation_time,ad_delivery_start_time,ad_delivery_stop_time,ad_creative_bodies,ad_creative_link_captions,ad_creative_link_descriptions,ad_creative_link_titles,ad_snapshot_url,languages,publisher_platforms";

        var url = $"https://graph.facebook.com/v21.0/ads_archive?search_page_ids={Uri.EscapeDataString(pageId)}&ad_reached_countries=['EG']&ad_active_status=ALL&fields={fields}&limit=50";

        while (!string.IsNullOrWhiteSpace(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorEl))
            {
                var msg = errorEl.TryGetProperty("message", out var m) ? m.GetString() : "Meta API error";
                var code = errorEl.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                throw new InvalidOperationException($"Meta Ad Library API error (code {code}): {msg}");
            }

            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in dataEl.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;

                string? title = null;
                if (item.TryGetProperty("ad_creative_link_titles", out var titlesEl) &&
                    titlesEl.ValueKind == JsonValueKind.Array &&
                    titlesEl.GetArrayLength() > 0)
                {
                    title = titlesEl[0].GetString();
                }

                string? body = null;
                if (item.TryGetProperty("ad_creative_bodies", out var bodiesEl) &&
                    bodiesEl.ValueKind == JsonValueKind.Array &&
                    bodiesEl.GetArrayLength() > 0)
                {
                    body = bodiesEl[0].GetString();
                }

                string? linkUrl = null;
                if (item.TryGetProperty("ad_creative_link_captions", out var capEl) &&
                    capEl.ValueKind == JsonValueKind.Array &&
                    capEl.GetArrayLength() > 0)
                {
                    linkUrl = capEl[0].GetString();
                }

                string? snapshotUrl = item.TryGetProperty("ad_snapshot_url", out var snapEl)
                    ? snapEl.GetString()
                    : null;

                DateTimeOffset? startTime = null;
                if (item.TryGetProperty("ad_delivery_start_time", out var stEl) &&
                    DateTimeOffset.TryParse(stEl.GetString(), out var stVal))
                {
                    startTime = stVal.ToUniversalTime();
                }

                DateTimeOffset? stopTime = null;
                if (item.TryGetProperty("ad_delivery_stop_time", out var spEl) &&
                    DateTimeOffset.TryParse(spEl.GetString(), out var spVal))
                {
                    stopTime = spVal.ToUniversalTime();
                }

                // publisher_platforms: the platforms the ad ran on (FACEBOOK, INSTAGRAM, ...). Absent on some ads.
                IReadOnlyList<string>? placements = null;
                if (item.TryGetProperty("publisher_platforms", out var platformsEl) && platformsEl.ValueKind == JsonValueKind.Array)
                {
                    placements = platformsEl.EnumerateArray()
                        .Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty)
                        .ToList();
                }

                results.Add(new MetaAdRawItem(
                    id,
                    title,
                    body,
                    linkUrl,
                    snapshotUrl,
                    startTime,
                    stopTime,
                    item.GetRawText(),
                    placements));
            }

            // Follow paging.next if available
            string? nextUrl = null;
            if (root.TryGetProperty("paging", out var pagingEl) &&
                pagingEl.TryGetProperty("next", out var nextEl))
            {
                nextUrl = nextEl.GetString();
            }

            url = nextUrl;
        }

        return results;
    }
}
