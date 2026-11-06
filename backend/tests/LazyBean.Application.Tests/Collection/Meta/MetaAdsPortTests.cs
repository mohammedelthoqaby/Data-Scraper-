using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LazyBean.Collectors.Meta;

namespace LazyBean.Application.Tests.Collection.Meta;

// Offline tests for the C# port of meta_ads_collector. Live Facebook behaviour is checked
// separately (see MetaLiveCheckTests) because it depends on the network and on Meta's current
// responses.
public class MetaAdsPortTests
{
    private const string Homepage =
        """<html><script>{"LSD",[],{"token":"AVqTestLsd1"}}</script><script>{"__spin_r":1035000123,"__spin_t":1700000000,"__hsi":"7300000000000000000","__comet_req":29,"server_revision":1035000123}</script></html>""";

    [Fact]
    public void ExtractTokens_ReadsLsdRevisionAndSessionTokens()
    {
        var tokens = MetaTokens.ExtractTokens(Homepage);

        Assert.Equal("AVqTestLsd1", tokens["lsd"]);
        Assert.Equal("1035000123", tokens["__rev"]);
        Assert.Equal("1035000123", tokens["__spin_r"]);
        Assert.Equal("1700000000", tokens["__spin_t"]);
        Assert.Equal("7300000000000000000", tokens["__hsi"]);
        Assert.Equal("29", tokens["__comet_req"]);
    }

    [Fact]
    public void ExtractTokens_MissingLsd_LeavesItOutSoBootstrapCanFail()
    {
        var tokens = MetaTokens.ExtractTokens("<html>no tokens here</html>");

        Assert.False(tokens.ContainsKey("lsd"));
    }

    [Theory]
    [InlineData("", "2893")]
    [InlineData("abc", "296")]
    public void CalculateJazoest_MatchesLibraryFormula(string lsd, string expected)
    {
        Assert.Equal(expected, MetaTokens.CalculateJazoest(lsd));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "a")]
    [InlineData(36, "10")]
    [InlineData(1295, "zz")]
    public void EncodeRequestId_UsesBase36(int counter, string expected)
    {
        Assert.Equal(expected, MetaTokens.EncodeRequestId(counter));
    }

    [Fact]
    public void Parse_LiveFlatLayout_MapsPrimaryCreativeAndDates()
    {
        var ad = MetaAdParser.Parse(Json("""
            {
              "ad_archive_id": "1001",
              "page_id": "102631558265863",
              "ad_delivery_start_time": 1700000000,
              "ad_delivery_stop_time": "2026-03-01T10:00:00Z",
              "is_active": true,
              "body": { "text": "عرض خاص 250 جنيه" },
              "title": "Strong Coffee",
              "link_url": "https://strongcoffee-eg.com/offer",
              "images": [ { "original_image_url": "https://img.example/a.jpg", "resized_image_url": "https://img.example/a-small.jpg" } ],
              "videos": [],
              "publisher_platforms": [ "FACEBOOK", "INSTAGRAM" ],
              "snapshot_url": "https://www.facebook.com/ads/library/?id=1001"
            }
            """));

        Assert.Equal("1001", ad.Id);
        Assert.Equal("2023-11-14T22:13:20+00:00", ad.StartDate);
        Assert.Equal("2026-03-01T10:00:00+00:00", ad.StopDate);
        Assert.Equal("Active", ad.Status);
        Assert.Equal("عرض خاص 250 جنيه", ad.Body);
        Assert.Equal("Strong Coffee", ad.Title);
        Assert.Equal("https://strongcoffee-eg.com/offer", ad.Link);
        Assert.Equal("https://img.example/a.jpg", ad.ImageUrl);
        Assert.Equal("https://www.facebook.com/ads/library/?id=1001", ad.SnapshotUrl);
        Assert.Equal(new[] { "FACEBOOK", "INSTAGRAM" }, ad.PublisherPlatforms.ToArray());
    }

    [Fact]
    public void Parse_InactiveAd_IsMarkedInactive_AndFallsBackToLibraryUrl()
    {
        var ad = MetaAdParser.Parse(Json("""{ "id": 2002, "is_active": false, "body": "plain body" }"""));

        Assert.Equal("2002", ad.Id);
        Assert.Equal("Inactive", ad.Status);
        Assert.Equal("plain body", ad.Body);
        Assert.Equal("https://www.facebook.com/ads/library/?id=2002", ad.SnapshotUrl);
        Assert.Null(ad.StartDate);
    }

    [Fact]
    public void Parse_AdStatusWithoutIsActive_IsUsedWhenPresent()
    {
        var active = MetaAdParser.Parse(Json("""{ "id": "3", "ad_status": "ACTIVE" }"""));
        var inactive = MetaAdParser.Parse(Json("""{ "id": "4", "ad_status": "INACTIVE" }"""));

        Assert.Equal("Active", active.Status);
        Assert.Equal("Inactive", inactive.Status);
    }

    [Fact]
    public void Parse_CardsLayout_PrefersResizedImage()
    {
        var ad = MetaAdParser.Parse(Json("""
            {
              "id": "5",
              "cards": [ {
                "body": { "text": "card body" },
                "title": "card title",
                "link_url": "https://example.com/card",
                "resized_image_url": "https://img.example/card-small.jpg",
                "original_image_url": "https://img.example/card.jpg"
              } ]
            }
            """));

        Assert.Equal("card body", ad.Body);
        Assert.Equal("card title", ad.Title);
        Assert.Equal("https://example.com/card", ad.Link);
        Assert.Equal("https://img.example/card-small.jpg", ad.ImageUrl);
    }

    [Fact]
    public void Parse_LegacyLayout_UsesCopyArraysAndSnapshotCard()
    {
        // No snapshot here: an overlaid snapshot.cards would route the ad through the cards layout,
        // exactly as the Python from_graphql_response does.
        var ad = MetaAdParser.Parse(Json("""
            {
              "id": "6",
              "ad_creative_bodies": [ "legacy body" ],
              "ad_creative_link_titles": [ "legacy title" ]
            }
            """));

        Assert.Equal("legacy body", ad.Body);
        Assert.Equal("legacy title", ad.Title);
        Assert.Null(ad.ImageUrl);
    }

    [Fact]
    public async Task CollectPage_PaginatesWithCursorAndSendsPageFilter()
    {
        var facebook = new FakeFacebookHandler(graphql: Queue(
            Page(["1001", "1002"], hasNext: true, cursor: "cursor-1"),
            Page(["1003"], hasNext: false, cursor: null)));

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("102631558265863", "EG", "ALL", 50);

        Assert.Equal("Success", result.Status);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "1001", "1002", "1003" }, result.Ads.Select(a => a.Id).ToArray());

        var graphqlCalls = facebook.Requests.Where(r => r.Url.Contains("/api/graphql/")).ToList();
        Assert.Equal(2, graphqlCalls.Count);

        var firstVariables = VariablesOf(graphqlCalls[0].Body);
        Assert.False(firstVariables.TryGetProperty("cursor", out _));
        Assert.Equal("PAGE", firstVariables.GetProperty("searchType").GetString());
        Assert.Equal("ALL", firstVariables.GetProperty("activeStatus").GetString());
        Assert.Equal("EG", firstVariables.GetProperty("countries")[0].GetString());
        Assert.Equal("102631558265863", firstVariables.GetProperty("pageIDs")[0].GetString());
        Assert.Equal("SORT_BY_TOTAL_IMPRESSIONS", firstVariables.GetProperty("sortData").GetProperty("mode").GetString());

        var secondVariables = VariablesOf(graphqlCalls[1].Body);
        Assert.Equal("cursor-1", secondVariables.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task CollectPage_RespectsMaxResults()
    {
        var facebook = new FakeFacebookHandler(graphql: Queue(
            Page(["1001", "1002"], hasNext: true, cursor: "cursor-1"),
            Page(["1003", "1004"], hasNext: false, cursor: null)));

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("123", "EG", "ALL", maxResults: 3);

        Assert.Equal("Success", result.Status);
        Assert.Equal(3, result.TotalCollected);
    }

    [Fact]
    public async Task CollectPage_ZeroAds_IsSuccessNotBlocked()
    {
        var facebook = new FakeFacebookHandler(graphql: Queue(Page([], hasNext: false, cursor: null)));

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("102631558265863", "EG", "ALL", 50);

        Assert.Equal("Success", result.Status);
        Assert.Equal(0, result.TotalCollected);
    }

    [Fact]
    public async Task CollectPage_PersistentRateLimit_IsBlockedWithRateLimitError()
    {
        const string rateLimited = """{"errors":[{"code":1675004,"message":"Rate limit exceeded"}]}""";
        var facebook = new FakeFacebookHandler(graphql: _ => rateLimited);

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("123", "EG", "ALL", 50);

        Assert.Equal("Blocked", result.Status);
        Assert.Equal("RateLimitError", result.ErrorType);
        Assert.StartsWith("Rate limit encountered from Meta", result.Error);
        Assert.Empty(result.Ads);
    }

    [Fact]
    public async Task CollectPage_PersistentSessionError_IsBlockedWithSessionExpiredError()
    {
        const string sessionError = """{"errors":[{"code":1357004,"message":"Session expired"}]}""";
        var facebook = new FakeFacebookHandler(graphql: _ => sessionError);

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("123", "EG", "ALL", 50);

        Assert.Equal("Blocked", result.Status);
        Assert.Equal("SessionExpiredError", result.ErrorType);
        Assert.Contains("Session or verification challenge from Meta", result.Error);
    }

    [Fact]
    public async Task CollectPage_HomepageBootstrapFailure_IsBlockedWithAuthenticationError()
    {
        var facebook = new FakeFacebookHandler(homepageStatus: HttpStatusCode.InternalServerError);

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("123", "EG", "ALL", 50);

        Assert.Equal("Blocked", result.Status);
        Assert.Equal("AuthenticationError", result.ErrorType);
        Assert.Contains("Homepage bootstrap failed (HTTP 500)", result.Error);
    }

    [Fact]
    public async Task CollectPage_RepeatedCursor_IsFailedNotBlocked()
    {
        var facebook = new FakeFacebookHandler(graphql: Queue(
            Page(["1001"], hasNext: true, cursor: "same"),
            Page(["1002"], hasNext: true, cursor: "same")));

        var result = await new MetaAdsPageCollector(MetaAdsOptions.NoWaits, facebook)
            .CollectPageAsync("123", "EG", "ALL", 50);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("MetaAdsError", result.ErrorType);
        Assert.Contains("Repeated pagination cursor detected", result.Error);
    }

    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    private static string Page(string[] adIds, bool hasNext, string? cursor)
    {
        var ads = string.Join(",", adIds.Select(id =>
            $$"""{"ad_archive_id":"{{id}}","is_active":true,"body":{"text":"body {{id}}"},"title":"title {{id}}","images":[{"original_image_url":"https://img.example/{{id}}.jpg"}],"videos":[]}"""));
        var endCursor = cursor is null ? "null" : $"\"{cursor}\"";
        var hasNextText = hasNext ? "true" : "false";

        return "{\"data\":{\"ad_library_main\":{\"search_results_connection\":{\"edges\":[{\"node\":{\"collated_results\":["
            + ads
            + "]}}],\"page_info\":{\"has_next_page\":" + hasNextText + ",\"end_cursor\":" + endCursor + "}}}}}";
    }

    private static Func<HttpRequestMessage, string> Queue(params string[] responses)
    {
        var index = 0;
        return _ =>
        {
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return response;
        };
    }

    private static JsonElement VariablesOf(string? formBody)
    {
        var fields = ParseForm(formBody ?? string.Empty);
        return JsonDocument.Parse(fields["variables"]).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(pair[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            result[name] = value;
        }

        return result;
    }

    // Serves the homepage bootstrap and GraphQL responses, and records every request body.
    private sealed class FakeFacebookHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string>? _graphql;
        private readonly HttpStatusCode _homepageStatus;

        public FakeFacebookHandler(
            Func<HttpRequestMessage, string>? graphql = null,
            HttpStatusCode homepageStatus = HttpStatusCode.OK)
        {
            _graphql = graphql;
            _homepageStatus = homepageStatus;
        }

        public List<(string Method, string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var url = request.RequestUri!.ToString();
            Requests.Add((request.Method.Method, url, body));

            if (url.Contains("/api/graphql/", StringComparison.Ordinal))
            {
                var text = _graphql?.Invoke(request) ?? throw new InvalidOperationException("No GraphQL responder configured.");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(text, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(_homepageStatus)
            {
                Content = new StringContent(Homepage, Encoding.UTF8, "text/html"),
            };
        }
    }
}
