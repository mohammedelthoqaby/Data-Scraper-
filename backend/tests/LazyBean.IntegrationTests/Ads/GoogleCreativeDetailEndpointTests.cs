using System.Net.Http.Headers;
using System.Net.Http.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LazyBean.IntegrationTests.Ads;

// The creative-detail step end to end on the real API host: a creative the listing flags is read, its detail is stored, the ad
// is linked again from it, and the same listing does not ask for the detail again. A detail for a creative the server does not
// hold is counted as not found.
[Collection("Database")]
public class GoogleCreativeDetailEndpointTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-google-detail-user";
    private const string Password = "P@ssw0rd-GoogleDetail-Tests!";
    private const string AdvertiserId = "AR09999999999999999991";

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
                var competitor = new Competitor { Key = $"google-detail-it-{Guid.NewGuid():N}", Name = "Strong Coffee (integration)" };
                db.Competitors.Add(competitor);
                db.AdvertiserIdentities.Add(new AdvertiserIdentity
                {
                    CompetitorId = competitor.Id,
                    Platform = AdPlatform.Google,
                    ExternalId = AdvertiserId,
                    Name = "Strong Coffee",
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
            new { username = Username, password = Password, deviceName = "google-detail-integration" });
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
    public async Task FlaggedCreative_IsReadAndStored_AndRelinkedFromItsDetail()
    {
        var externalId = NewCreativeId();
        var lastSeen = RecentSecond();

        var first = await IngestAsync(externalId, lastSeen);
        Assert.True(Assert.Single(first.Items!).NeedsDetail);

        var stored = await PostDetailsAsync(new GoogleCreativeDetailDto(
            externalId,
            Headline: "Brasile 250g",
            Description: null,
            LandingUrl: "https://strongcoffee-eg.com/products/brasile-250g",
            PreviewUrl: "https://tpc.googlesyndication.com/archive/simgad/77",
            Variations: ["https://tpc.googlesyndication.com/archive/simgad/77"],
            LastShownAt: lastSeen));

        Assert.Equal(1, stored.Updated);
        Assert.Equal(0, stored.NotFound);

        var ad = await LoadAsync(externalId);
        Assert.Equal("Brasile 250g", ad.Headline);
        Assert.Equal("https://strongcoffee-eg.com/products/brasile-250g", ad.LinkUrl);
        Assert.Equal("https://tpc.googlesyndication.com/archive/simgad/77", ad.PreviewUrl);
        Assert.NotNull(ad.DetailFetchedAt);
        // The catalog has no product for this competitor, so a product page with a copy is "no matching product".
        Assert.Equal(AdLinkReasons.NoMatch, ad.LinkReason);

        // The listing shows the creative again with the same last-seen time: its detail is not asked for again.
        var again = await IngestAsync(externalId, lastSeen);
        Assert.False(Assert.Single(again.Items!).NeedsDetail);
    }

    [Fact]
    public async Task ADetailForACreativeTheServerDoesNotHold_IsCountedAsNotFound()
    {
        var result = await PostDetailsAsync(new GoogleCreativeDetailDto(
            NewCreativeId(), null, null, null, null, [], null));

        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.NotFound);
    }

    private static string NewCreativeId() => $"CRDETAIL{Guid.NewGuid():N}"[..24];

    private static DateTimeOffset RecentSecond() =>
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300);

    private async Task<AdsIngestResponse> IngestAsync(string externalId, DateTimeOffset lastSeen)
    {
        var item = new GoogleAdPayloadItem(
            externalId,
            "image",
            "Strong Coffee",
            null,
            null,
            null,
            lastSeen.AddDays(-30),
            lastSeen,
            null,
            null);

        using var response = await SendAsync(HttpMethod.Post, "/api/collector/ads/google",
            new GoogleAdsIngestRequest(AdvertiserId, "Success", null, null, [item]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdsIngestResponse>())!;
    }

    private async Task<GoogleCreativeDetailsResponse> PostDetailsAsync(GoogleCreativeDetailDto detail)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/collector/google/creative-details",
            new GoogleCreativeDetailsRequest(AdvertiserId, [detail]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoogleCreativeDetailsResponse>())!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await _client.SendAsync(request);
    }

    private async Task<Ad> LoadAsync(string externalId)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        return await db.Ads.AsNoTracking().SingleAsync(a => a.Platform == AdPlatform.Google && a.ExternalAdId == externalId);
    }
}
