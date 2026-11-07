using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LazyBean.IntegrationTests.Ads;

// The ad images end to end on the real API host: the desktop app uploads them (checked, replaced in place), the image is served
// with its cache headers and an ETag, the gallery shows identical ads as one card, and the images of an ad not seen for 90 days
// are removed.
[Collection("Database")]
public class AdThumbnailEndpointTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-ad-thumbnail-user";
    private const string Password = "P@ssw0rd-AdThumbnail-Tests!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _device = null!;
    private HttpClient _dashboard = null!;
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
            new { username = Username, password = Password, deviceName = "ad-thumbnail-integration" });
        pair.EnsureSuccessStatusCode();
        _token = (await pair.Content.ReadFromJsonAsync<PairResponse>())!.Token;

        _device = _factory.CreateClient();
        _dashboard = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await _dashboard.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        login.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _device.Dispose();
        _dashboard.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task UploadedImages_AreServed_WithCacheHeadersAndAnEtag_AndRevalidatedTo304()
    {
        var store = await NewStoreAsync();
        var ad = await NewAdAsync(store.CompetitorId, store.IdentityId, "image-cache", DateTimeOffset.UtcNow);
        var jpeg = Jpeg(480, 360);

        var upload = await SendAsync(HttpMethod.Post, "/api/collector/ads/thumbnails", _token,
            new AdThumbnailUploadRequest("Google", store.ExternalId, [new AdThumbnailItem(ad.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/77", [Convert.ToBase64String(jpeg)])]));
        upload.EnsureSuccessStatusCode();
        var result = (await upload.Content.ReadFromJsonAsync<AdThumbnailUploadResponse>())!;
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.ImagesStored);

        var image = await _dashboard.GetAsync($"/api/ads/{ad.Id}/thumbnail");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(jpeg, await image.Content.ReadAsByteArrayAsync());
        // The server sends "private, max-age=604800"; the framework may list the directives in another order.
        Assert.True(image.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromSeconds(604800), image.Headers.CacheControl.MaxAge);
        var etag = image.Headers.ETag!.Tag;
        Assert.Matches("^\"[0-9a-f]{64}\"$", etag);

        using var revalidate = new HttpRequestMessage(HttpMethod.Get, $"/api/ads/{ad.Id}/thumbnail");
        revalidate.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await _dashboard.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task AnImageThatIsNotAJpeg_IsRefused_AndTheOthersAreStored()
    {
        var store = await NewStoreAsync();
        var good = await NewAdAsync(store.CompetitorId, store.IdentityId, "refuse-good", DateTimeOffset.UtcNow);
        var bad = await NewAdAsync(store.CompetitorId, store.IdentityId, "refuse-bad", DateTimeOffset.UtcNow);

        var upload = await SendAsync(HttpMethod.Post, "/api/collector/ads/thumbnails", _token,
            new AdThumbnailUploadRequest("Google", store.ExternalId,
            [
                new AdThumbnailItem(good.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/1", [Convert.ToBase64String(Jpeg(300, 250))]),
                new AdThumbnailItem(bad.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/2", [Convert.ToBase64String(Encoding.ASCII.GetBytes("GIF89a"))]),
            ]));
        upload.EnsureSuccessStatusCode();
        var result = (await upload.Content.ReadFromJsonAsync<AdThumbnailUploadResponse>())!;

        Assert.Equal(1, result.Stored);
        var refused = Assert.Single(result.Rejected);
        Assert.Equal(bad.ExternalAdId, refused.ExternalAdId);
        Assert.Equal("The image is not a JPEG.", refused.Reason);
    }

    [Fact]
    public async Task AWiderImage_IsRefused()
    {
        var store = await NewStoreAsync();
        var ad = await NewAdAsync(store.CompetitorId, store.IdentityId, "too-wide", DateTimeOffset.UtcNow);

        var upload = await SendAsync(HttpMethod.Post, "/api/collector/ads/thumbnails", _token,
            new AdThumbnailUploadRequest("Google", store.ExternalId,
            [new AdThumbnailItem(ad.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/3", [Convert.ToBase64String(Jpeg(900, 600))])]));
        upload.EnsureSuccessStatusCode();
        var result = (await upload.Content.ReadFromJsonAsync<AdThumbnailUploadResponse>())!;

        Assert.Equal(0, result.Stored);
        Assert.Contains("wider", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task IdenticalAdsFromOneStore_AreOneGalleryCard_WithTheirVariations()
    {
        var store = await NewStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var first = await NewAdAsync(store.CompetitorId, store.IdentityId, "dup-1", now, title: "Brasile 250g", body: "Smooth coffee");
        var second = await NewAdAsync(store.CompetitorId, store.IdentityId, "dup-2", now.AddMinutes(-5), title: "Brasile 250g", body: "Smooth coffee");

        var jpeg = Convert.ToBase64String(Jpeg(480, 360));
        var upload = await SendAsync(HttpMethod.Post, "/api/collector/ads/thumbnails", _token,
            new AdThumbnailUploadRequest("Google", store.ExternalId,
            [
                new AdThumbnailItem(first.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/9", [jpeg]),
                new AdThumbnailItem(second.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/9", [jpeg]),
            ]));
        upload.EnsureSuccessStatusCode();

        var gallery = await _dashboard.GetFromJsonAsync<AdGalleryResponseDto>($"/api/ads/gallery?competitorId={store.CompetitorId}&pageSize=24");

        Assert.NotNull(gallery);
        var card = Assert.Single(gallery!.Items.Where(c => c.Headline == "Brasile 250g"));
        Assert.Equal(2, card.VariationCount);
        Assert.Equal(first.Id, card.Id);
        Assert.Equal(1, card.ThumbnailCount);
        Assert.Equal(store.CompetitorName, card.CompetitorName);
    }

    [Fact]
    public async Task ImagesOfAdsNotSeenFor90Days_AreRemoved()
    {
        var store = await NewStoreAsync();
        var old = await NewAdAsync(store.CompetitorId, store.IdentityId, "retention-old", DateTimeOffset.UtcNow.AddDays(-120));
        var fresh = await NewAdAsync(store.CompetitorId, store.IdentityId, "retention-fresh", DateTimeOffset.UtcNow);

        await SendAsync(HttpMethod.Post, "/api/collector/ads/thumbnails", _token,
            new AdThumbnailUploadRequest("Google", store.ExternalId,
            [
                new AdThumbnailItem(old.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/4", [Convert.ToBase64String(Jpeg(300, 250))]),
                new AdThumbnailItem(fresh.ExternalAdId, "tpc.googlesyndication.com/archive/simgad/5", [Convert.ToBase64String(Jpeg(300, 250))]),
            ]));

        // The old ad's last-seen time is 120 days back, so it is past the retention period. The upload above stored both.
        using (var scope = _factory.Services.CreateScope())
        {
            var thumbnails = scope.ServiceProvider.GetRequiredService<IAdThumbnailService>();
            var result = await thumbnails.ApplyRetentionAsync(DateTimeOffset.UtcNow);
            Assert.True(result.AdsCleared >= 1);
        }

        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        Assert.False(await db.AdThumbnails.AnyAsync(t => t.AdId == old.Id));
        Assert.True(await db.AdThumbnails.AnyAsync(t => t.AdId == fresh.Id));
        var cleared = await db.Ads.AsNoTracking().SingleAsync(a => a.Id == old.Id);
        Assert.Equal(0, cleared.ThumbnailCount);
        Assert.Null(cleared.ThumbnailSourceKey);
    }

    private async Task<(Guid CompetitorId, Guid IdentityId, string ExternalId, string CompetitorName)> NewStoreAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var externalId = $"AR{Guid.NewGuid():N}"[..22];
        var competitor = new Competitor { Key = $"thumb-it-{Guid.NewGuid():N}", Name = "Thumbnail store (integration)" };
        var identity = new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Google,
            ExternalId = externalId,
            Name = "Thumbnail store",
            Status = AdvertiserIdentityStatus.Approved,
        };
        db.Competitors.Add(competitor);
        db.AdvertiserIdentities.Add(identity);
        await db.SaveChangesAsync();
        return (competitor.Id, identity.Id, externalId, competitor.Name);
    }

    private async Task<Ad> NewAdAsync(
        Guid competitorId,
        Guid identityId,
        string externalAdId,
        DateTimeOffset lastSeen,
        string? title = null,
        string? body = null)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var ad = new Ad
        {
            CompetitorId = competitorId,
            IdentityId = identityId,
            ExternalAdId = $"{externalAdId}-{Guid.NewGuid():N}",
            Platform = AdPlatform.Google,
            // A Google card's copy comes from the creative's detail, so the test stores it there.
            Headline = title,
            Description = body,
            Status = AdStatus.Active,
            FirstSeenAt = lastSeen.AddDays(-10),
            LastSeenAt = lastSeen,
            CreativeHash = externalAdId,
        };
        db.Ads.Add(ad);
        await db.SaveChangesAsync();
        return ad;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _device.SendAsync(request);
    }

    // A JPEG made of the frame header and padding only: enough for the header and size rules.
    private static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
            0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        bytes.AddRange([0xFF, 0xD9]);
        return bytes.ToArray();
    }
}
