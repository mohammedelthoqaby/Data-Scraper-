using System.Net;
using System.Net.Http.Json;
using LazyBean.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LazyBean.IntegrationTests;

// Boots the real Api host (WebApplicationFactory<Program>) against the dedicated
// "lazybean_test" database (TestDatabaseFixture) - never the dev database - with a
// dedicated test user seeded directly, not relying on DbInitializer's first-boot seed.
[Collection("Database")]
public class AuthTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-auth-test-user";
    private const string Password = "P@ssw0rd-For-Tests-Only!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        if (existing is null)
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
                    ["Admin:Password"] = ""
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Login_CorrectCredentials_ReturnsUserAndSetsCookie()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.StartsWith("lazybean.auth="));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUsername_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", new { username = "no-such-user", password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCompetitors_WithoutCookie_Returns401()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/competitors");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCompetitors_AfterLogin_Returns200()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.GetAsync("/api/competitors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_Unauthenticated_ReturnsFalseAndNull()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SessionResponseDto>();
        Assert.NotNull(body);
        Assert.False(body.Authenticated);
        Assert.Null(body.Username);
    }

    [Fact]
    public async Task Session_Authenticated_ReturnsTrueAndUsername()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SessionResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.Authenticated);
        Assert.Equal(Username, body.Username);
    }

    private sealed record SessionResponseDto(bool Authenticated, string? Username);
}
