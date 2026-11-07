using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LazyBean.IntegrationTests;

// Changing the dashboard password against the real API host and the test Postgres: the current password is required,
// other browser sessions end, collector apps keep working unless asked to revoke, the change is rate limited, and the
// startup bootstrap and Admin__ResetPassword recovery behave as documented.
[Collection("Database")]
public class ChangePasswordTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string OriginalPassword = "Original-Password-1";
    private const string NewPassword = "Brand-New-Password-2";

    private WebApplicationFactory<Program> _factory = null!;

    private sealed record PairResponse(Guid DeviceId, string DeviceName, string Token, IReadOnlyList<string> Scopes);
    private sealed record ChangeResponse(DateTimeOffset PasswordChangedAt, int DevicesRevoked);
    private sealed record AccountResponse(Guid Id, string Username, DateTimeOffset? PasswordChangedAt);

    public Task InitializeAsync()
    {
        _factory = CreateFactory(new Dictionary<string, string?>());
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ChangePassword_WithTheCurrentPassword_ChangesIt()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        var change = await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(_factory, username, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(_factory, username, OriginalPassword));
    }

    [Fact]
    public async Task ChangePassword_KeepsThisBrowserSignedIn()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SignsOutEveryOtherBrowserSession()
    {
        var username = await CreateUserAsync();
        using var thisBrowser = await SignedInClientAsync(_factory, username, OriginalPassword);
        using var otherBrowser = await SignedInClientAsync(_factory, username, OriginalPassword);

        var change = await thisBrowser.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await thisBrowser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_IsRefused_AndTheSessionStaysSignedIn()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody("not-the-password", NewPassword));

        // 400, not 401: a 401 would make the dashboard treat the session as expired.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("CurrentPassword", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(_factory, username, OriginalPassword));
    }

    [Fact]
    public async Task ChangePassword_TooShort_IsRefused()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, "short-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("NewPassword", out _));
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(_factory, username, OriginalPassword));
    }

    [Fact]
    public async Task ChangePassword_ByDefault_LeavesCollectorAppsWorking()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);
        var device = await PairDeviceAsync(username, OriginalPassword);

        await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, NewPassword));

        var poll = await SendDeviceAsync(device.Token);
        Assert.True(poll.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK, $"got {poll.StatusCode}");
    }

    [Fact]
    public async Task ChangePassword_WithRevokeDevices_SignsOutTheCollectorApps()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);
        var device = await PairDeviceAsync(username, OriginalPassword);

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            ChangeBody(OriginalPassword, NewPassword, revokeDevices: true));

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var body = await change.Content.ReadFromJsonAsync<ChangeResponse>();
        Assert.True(body!.DevicesRevoked >= 1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendDeviceAsync(device.Token)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_IsRateLimited_AfterFiveAttempts()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        var last = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            last = (await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody("wrong-password-1", NewPassword))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Account_ShowsWhenThePasswordWasLastChanged()
    {
        var username = await CreateUserAsync();
        using var client = await SignedInClientAsync(_factory, username, OriginalPassword);

        var before = await client.GetFromJsonAsync<AccountResponse>("/api/auth/account");
        Assert.Null(before!.PasswordChangedAt);

        await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(OriginalPassword, NewPassword));

        var after = await client.GetFromJsonAsync<AccountResponse>("/api/auth/account");
        Assert.Equal(username, after!.Username);
        Assert.NotNull(after.PasswordChangedAt);
    }

    [Fact]
    public async Task Restart_WithTheSameAdminPassword_KeepsThePasswordChangedOnTheWebsite()
    {
        var username = UniqueUsername();
        await StartAsync(AdminSettings(username, OriginalPassword));
        await ChangeOnWebsiteAsync(username, OriginalPassword, NewPassword);

        await using var restarted = CreateFactory(AdminSettings(username, OriginalPassword));

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(restarted, username, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(restarted, username, OriginalPassword));
    }

    [Fact]
    public async Task ResetPassword_AppliesOnce_AndDoesNotUndoALaterChangeOnTheWebsite()
    {
        var username = UniqueUsername();
        await StartAsync(AdminSettings(username, OriginalPassword));
        await ChangeOnWebsiteAsync(username, OriginalPassword, NewPassword);

        // Recovery: the configured password is applied at startup.
        await StartAsync(AdminSettings(username, "Recovered-Password-3", reset: true));
        await using (var recovered = CreateFactory(AdminSettings(username, "Recovered-Password-3", reset: true)))
        {
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(recovered, username, "Recovered-Password-3"));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(recovered, username, NewPassword));
        }

        // The variable is still set. A later change on the website must survive the next restart.
        await ChangeOnWebsiteAsync(username, "Recovered-Password-3", "Later-Password-4");
        await StartAsync(AdminSettings(username, "Recovered-Password-3", reset: true));
        await using var restarted = CreateFactory(AdminSettings(username, "Recovered-Password-3", reset: true));
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(restarted, username, "Later-Password-4"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(restarted, username, "Recovered-Password-3"));
    }

    private WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?> admin) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = testDb.ConnectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Admin:Username"] = "",
                    ["Admin:Password"] = "",
                };
                foreach (var (key, value) in admin)
                {
                    values[key] = value;
                }

                config.AddInMemoryCollection(values);
            });
        });

    private static Dictionary<string, string?> AdminSettings(string username, string password, bool reset = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Admin:Username"] = username,
            ["Admin:Password"] = password,
        };
        if (reset)
        {
            settings["Admin:ResetPassword"] = "true";
        }

        return settings;
    }

    // Starting the host runs the bootstrap seed (DbInitializer), which is what a real restart does.
    private async Task StartAsync(Dictionary<string, string?> admin)
    {
        await using var factory = CreateFactory(admin);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    private async Task ChangeOnWebsiteAsync(string username, string currentPassword, string newPassword)
    {
        using var client = await SignedInClientAsync(_factory, username, currentPassword);
        var change = await client.PostAsJsonAsync("/api/auth/change-password", ChangeBody(currentPassword, newPassword));
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
    }

    private static object ChangeBody(string currentPassword, string newPassword, bool revokeDevices = false) =>
        new { currentPassword, newPassword, revokeDevices };

    private static string UniqueUsername() => $"it-password-{Guid.NewGuid():N}";

    private async Task<string> CreateUserAsync()
    {
        var username = UniqueUsername();
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        db.Users.Add(new User { Username = username, PasswordHash = new PasswordHasherAdapter().Hash(OriginalPassword) });
        await db.SaveChangesAsync();
        return username;
    }

    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> factory, string username, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<HttpStatusCode> LoginStatusAsync(WebApplicationFactory<Program> factory, string username, string password)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        return (await client.PostAsJsonAsync("/api/auth/login", new { username, password })).StatusCode;
    }

    private async Task<PairResponse> PairDeviceAsync(string username, string password)
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync(
            "/api/collector/pair",
            new { username, password, deviceName = "Office PC" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PairResponse>())!;
    }

    private async Task<HttpResponseMessage> SendDeviceAsync(string token)
    {
        using var anonymous = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/collector/jobs/next");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await anonymous.SendAsync(request);
    }
}
