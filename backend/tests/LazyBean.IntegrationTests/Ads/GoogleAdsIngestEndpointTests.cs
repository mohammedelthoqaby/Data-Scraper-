using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Collectors.Google;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.IntegrationTests.Ads;

// POST /api/collector/ads/google on the real API host and test Postgres. The creatives come from a saved
// Ads Transparency SearchCreatives response for AR01064852753525768193 (region EG), parsed by the production
// GoogleAdsTransparencyClient and mapped to the payload the desktop sends.
[Collection("Database")]
public class GoogleAdsIngestEndpointTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-google-ads-user";
    private const string Password = "P@ssw0rd-GoogleAds-Tests!";
    private const string AdvertiserId = "AR01064852753525768193";

    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "google_search_AR01064852753525768193.json");

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = null!;

    private sealed record PairResponse(Guid DeviceId, string DeviceName, string Token, IReadOnlyList<string> Scopes);

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using (var db = new LazyBeanDbContext(options))
        {
            if (!await db.Users.AnyAsync(u => u.Username == Username))
            {
                db.Users.Add(new User { Username = Username, PasswordHash = new PasswordHasherAdapter().Hash(Password) });
                await db.SaveChangesAsync();
            }

            if (!await db.AdvertiserIdentities.AnyAsync(i => i.Platform == AdPlatform.Google && i.ExternalId == AdvertiserId))
            {
                var competitor = new Competitor { Key = $"google-it-{Guid.NewGuid():N}", Name = "CAFELAX (integration)" };
                db.Competitors.Add(competitor);
                db.AdvertiserIdentities.Add(new AdvertiserIdentity
                {
                    CompetitorId = competitor.Id,
                    Platform = AdPlatform.Google,
                    ExternalId = AdvertiserId,
                    Name = "CAFELAX",
                    Status = AdvertiserIdentityStatus.Approved,
                });
                await db.SaveChangesAsync();
            }
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = testDb.ConnectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Admin:Username"] = "",
                    ["Admin:Password"] = "",
                });
            });
        });

        using var anonymous = _factory.CreateClient();
        var pair = await anonymous.PostAsJsonAsync(
            "/api/collector/pair",
            new { username = Username, password = Password, deviceName = "google-ads-integration" });
        pair.EnsureSuccessStatusCode();
        _token = (await pair.Content.ReadFromJsonAsync<PairResponse>())!.Token;

        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task RealAdsTransparencyResponse_IsAcceptedAndEveryDistinctCreativeIsStored()
    {
        var creatives = await ParseFixtureAsync();
        var distinctIds = creatives.Select(c => c.ExternalAdId).Distinct().ToList();

        var body = await AssertSuccessAsync(await PostAsync(creatives.Select(ToPayload)));

        Assert.Equal(creatives.Count, body.Items!.Count);
        Assert.DoesNotContain(body.Items, i => i.Outcome == "skipped");
        Assert.Equal(distinctIds.Count, await CountGoogleAdsAsync(distinctIds));
    }

    [Fact]
    public async Task ImageOnlyAndVideoCreatives_FromTheRealResponse_AreStored()
    {
        var creatives = await ParseFixtureAsync();
        var imageAndVideo = creatives.Where(c => c.FormatName is "image" or "video").ToList();
        Assert.NotEmpty(imageAndVideo);
        Assert.Contains(imageAndVideo, c => c.FormatName == "video");

        var body = await AssertSuccessAsync(await PostAsync(imageAndVideo.Select(ToPayload)));

        Assert.Equal(imageAndVideo.Count, body.Items!.Count);
        Assert.DoesNotContain(body.Items, i => i.Outcome == "skipped");
        var ids = imageAndVideo.Select(c => c.ExternalAdId).Distinct().ToList();
        Assert.Equal(ids.Count, await CountGoogleAdsAsync(ids));
    }

    [Fact]
    public async Task ReplayOfTheSameResponse_IsAcceptedAndDoesNotDuplicateRows()
    {
        var creatives = await ParseFixtureAsync();
        var distinctIds = creatives.Select(c => c.ExternalAdId).Distinct().ToList();
        var items = creatives.Select(ToPayload).ToList();

        await AssertSuccessAsync(await PostAsync(items));
        var replay = await AssertSuccessAsync(await PostAsync(items));

        Assert.Equal(0, replay.IngestedCount);
        Assert.All(replay.Items!, i => Assert.Equal("updated", i.Outcome));
        Assert.Equal(distinctIds.Count, await CountGoogleAdsAsync(distinctIds));
    }

    [Fact]
    public async Task DuplicateCreativeInOneBatch_IsStoredOnce()
    {
        var id = $"CRDUP{Guid.NewGuid():N}";
        var item = new GoogleAdPayloadItem(id, "text", "Duplicate ad", null, null, null, null, null, "{}");

        var body = await AssertSuccessAsync(await PostAsync([item, item]));

        Assert.Equal(2, body.Items!.Count);
        Assert.Single(body.Items, i => i.Outcome == "created");
        Assert.Single(body.Items, i => i.Outcome == "duplicate");
        Assert.Equal(1, await CountGoogleAdsAsync([id]));
    }

    [Fact]
    public async Task BadItems_AreSkippedOrTrimmed_AndTheRestOfTheBatchIsStored()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var goodId = $"CRGOOD{suffix}";
        var longTitleId = $"CRLONGTITLE{suffix}";
        var longLinkId = $"CRLONGURL{suffix}";
        var nulId = $"CRNUL{suffix}";
        var surrogateId = $"CRSURROGATE{suffix}";
        var badJsonId = $"CRBADJSON{suffix}";

        var items = new List<GoogleAdPayloadItem>
        {
            new(goodId, "text", "Good ad", null,
                $"https://adstransparency.google.com/advertiser/{AdvertiserId}/creative/{goodId}?region=EG",
                null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{\"2\":\"good\"}"),
            new("", "text", "Blank creative id", null, null, null, null, null, "{}"),
            new(new string('9', 250), "text", "Creative id too long", null, null, null, null, null, "{}"),
            new(longTitleId, "text", new string('ع', 600), new string('x', 5000), null, null, null, null, "{}"),
            new(longLinkId, "video", "Long link", null, new string('a', 2500), "https://example.test/" + new string('b', 2500), null, null, "{}"),
            new(nulId, "text", "NUL in raw json", null, null, null, null, null, "{\"a\":\"\\u0000\"}"),
            new(surrogateId, "text", "Lone surrogate in raw json", null, null, null, null, null, "{\"a\":\"\\ud800\"}"),
            new(badJsonId, "image", "Raw json is not json", null, null, null, null, null, "{not json"),
        };

        var body = await AssertSuccessAsync(await PostAsync(items));

        Assert.Equal(2, body.Items!.Count(i => i.Outcome == "skipped"));
        Assert.Contains(body.Items!, i => i.Outcome == "skipped" && i.Reason == "The creative has no ID.");
        Assert.Contains(body.Items!, i => i.Outcome == "skipped" && i.Reason == "The creative ID is longer than 200 characters.");
        Assert.Equal(6, body.Items!.Count(i => i.Outcome is "created" or "updated"));

        var trimmedTitle = await LoadAdAsync(longTitleId);
        Assert.Equal(500, trimmedTitle.Title!.Length);

        var longLink = await LoadAdAsync(longLinkId);
        Assert.Null(longLink.LinkUrl);
        Assert.Null(longLink.SnapshotUrl);

        Assert.Null((await LoadAdAsync(nulId)).RawDataJson);
        Assert.Null((await LoadAdAsync(surrogateId)).RawDataJson);
        Assert.Null((await LoadAdAsync(badJsonId)).RawDataJson);
        Assert.NotNull((await LoadAdAsync(goodId)).RawDataJson);
    }

    private static async Task<List<GoogleCreativeDto>> ParseFixtureAsync()
    {
        var body = await File.ReadAllTextAsync(FixturePath);
        var client = new GoogleAdsTransparencyClient(
            new HttpClient(new FixedResponseHandler(body)),
            NullLogger<GoogleAdsTransparencyClient>.Instance);

        var page = await client.SearchCreativesAsync(AdvertiserId, null, CancellationToken.None);
        return page.Creatives.ToList();
    }

    // Same mapping as the desktop GoogleJobHandler.ToPayload.
    private static GoogleAdPayloadItem ToPayload(GoogleCreativeDto creative) => new(
        creative.ExternalAdId,
        creative.FormatName,
        creative.Title,
        creative.Body,
        creative.SnapshotUrl,
        creative.LinkUrl,
        creative.FirstSeen,
        creative.LastSeen,
        creative.RawJson);

    private Task<HttpResponseMessage> PostAsync(IEnumerable<GoogleAdPayloadItem> items) =>
        SendAsync(new GoogleAdsIngestRequest(AdvertiserId, "Success", null, null, items.ToList()));

    private async Task<HttpResponseMessage> SendAsync(GoogleAdsIngestRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collector/ads/google")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await _client.SendAsync(request);
    }

    private static async Task<AdsIngestResponse> AssertSuccessAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but got {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<AdsIngestResponse>(text, ReadOptions)!;
    }

    private async Task<int> CountGoogleAdsAsync(IReadOnlyCollection<string> externalIds)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var ids = externalIds.ToList();
        return await db.Ads.AsNoTracking()
            .CountAsync(a => a.Platform == AdPlatform.Google && ids.Contains(a.ExternalAdId));
    }

    private async Task<Ad> LoadAdAsync(string externalAdId)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        return await db.Ads.AsNoTracking()
            .SingleAsync(a => a.Platform == AdPlatform.Google && a.ExternalAdId == externalAdId);
    }

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
