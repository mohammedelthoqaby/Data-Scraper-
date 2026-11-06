using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LazyBean.IntegrationTests.Collector;

// End-to-end checks on the real API host and the test Postgres: pairing, the device-token and cookie
// trust boundary, revocation, the claim guard on store writes, the pair rate limit, and a two-device
// race for one job (which exercises the xmin concurrency token on collector_jobs).
[Collection("Database")]
public class CollectorEndpointTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-collector-user";
    private const string Password = "P@ssw0rd-For-Collector-Tests!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _cookieClient = null!;

    private sealed record PairResponse(Guid DeviceId, string DeviceName, string Token, IReadOnlyList<string> Scopes);

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        if (!await db.Users.AnyAsync(u => u.Username == Username))
        {
            db.Users.Add(new User { Username = Username, PasswordHash = new PasswordHasherAdapter().Hash(Password) });
            await db.SaveChangesAsync();
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

        _cookieClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    }

    public Task DisposeAsync()
    {
        _cookieClient.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Pair_ReturnsADeviceToken_ThatCanPollJobs()
    {
        var device = await PairAsync("Office PC");

        var response = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", device.Token);

        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK, $"got {response.StatusCode}");
    }

    [Fact]
    public async Task Pair_ReturnsOnlyTheDeviceScopes()
    {
        var device = await PairAsync("Office PC");

        Assert.Equal(new[] { CollectorDevice.AdsIngestScope, CollectorDevice.StoreIngestScope }, device.Scopes);
    }

    [Fact]
    public async Task Pair_WrongPassword_IsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/collector/pair",
            new { username = Username, password = "wrong", deviceName = "Office PC" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pair_IsRateLimited_AfterFiveAttempts()
    {
        using var anonymous = _factory.CreateClient();
        HttpStatusCode last = HttpStatusCode.OK;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await anonymous.PostAsJsonAsync(
                "/api/collector/pair",
                new { username = Username, password = "wrong", deviceName = "Office PC" });
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task CollectorEndpoints_RejectMissingAndUnknownTokens()
    {
        var anonymous = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", token: null);
        var bogus = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", token: "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task DeviceToken_CannotReachDashboardEndpoints()
    {
        var device = await PairAsync("Office PC");

        var devices = await SendAsync(HttpMethod.Get, "/api/collector-admin/devices", device.Token);
        var status = await SendAsync(HttpMethod.Get, "/api/collector-admin/status", device.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, devices.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
    }

    [Fact]
    public async Task DashboardCookie_CannotReachCollectorEndpoints()
    {
        await LoginAsDashboardAsync();

        var response = await _cookieClient.GetAsync("/api/collector/jobs/next");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"got {response.StatusCode}");
    }

    [Fact]
    public async Task RevokedDeviceToken_StopsWorking()
    {
        var device = await PairAsync("Office PC");
        await LoginAsDashboardAsync();

        var revoke = await _cookieClient.DeleteAsync($"/api/collector-admin/devices/{device.DeviceId}");
        var afterRevoke = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", device.Token);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task StoreProductWrites_WithoutAClaim_AreConflicts()
    {
        var device = await PairAsync("Office PC");

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/collector/runs/{Guid.NewGuid()}/competitors/strong-coffee/products",
            device.Token,
            new { page = 1, totalPages = 1, products = Array.Empty<object>() });

        Assert.True(
            response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound,
            $"got {response.StatusCode}");
    }

    [Fact]
    public async Task SingleDevice_ClaimsAQueuedJob()
    {
        var device = await PairAsync("Office PC");
        var jobId = await QueueStoreJobAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", device.Token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"status={response.StatusCode} body={body}");
        var job = await ReadJobAsync(jobId);
        Assert.Equal(CollectorJobStatus.Claimed, job.Status);
    }

    [Fact]
    public async Task TwoDevices_RacingForOneJob_OnlyOneWinsIt()
    {
        var first = await PairAsync("Office PC");
        var second = await PairAsync("Warehouse PC");
        var jobId = await QueueStoreJobAsync();

        var results = await Task.WhenAll(
            SendAsync(HttpMethod.Get, "/api/collector/jobs/next", first.Token),
            SendAsync(HttpMethod.Get, "/api/collector/jobs/next", second.Token));

        Assert.True(results.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, string.Join(", ", results.Select(r => r.StatusCode + " " + r.Content.ReadAsStringAsync().GetAwaiter().GetResult())));

        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var job = await db.CollectorJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(CollectorJobStatus.Claimed, job.Status);
        Assert.Contains(job.ClaimedByDeviceId, new[] { (Guid?)first.DeviceId, second.DeviceId });
    }

    [Fact]
    public async Task AdsNow_FromADevice_QueuesTheAdsJobs()
    {
        var device = await PairAsync("Office PC");

        var response = await SendAsync(HttpMethod.Post, "/api/collector/ads-now", device.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Unpair_RevokesTheCallingDevice()
    {
        var device = await PairAsync("Office PC");

        var unpair = await SendAsync(HttpMethod.Post, "/api/collector/unpair", device.Token);
        var afterwards = await SendAsync(HttpMethod.Get, "/api/collector/jobs/next", device.Token);

        Assert.Equal(HttpStatusCode.NoContent, unpair.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    private async Task<PairResponse> PairAsync(string deviceName)
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync(
            "/api/collector/pair",
            new { username = Username, password = Password, deviceName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PairResponse>())!;
    }

    private async Task LoginAsDashboardAsync()
    {
        var response = await _cookieClient.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var anonymous = _factory.CreateClient();
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await anonymous.SendAsync(request);
    }

    private async Task<CollectorJob> ReadJobAsync(Guid jobId)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        return await db.CollectorJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private async Task<Guid> QueueStoreJobAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var job = new CollectorJob
        {
            GroupId = Guid.NewGuid(),
            Kind = CollectorJobKind.StoreCollection,
            Sequence = 1,
            Status = CollectorJobStatus.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };
        db.CollectorJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }
}
