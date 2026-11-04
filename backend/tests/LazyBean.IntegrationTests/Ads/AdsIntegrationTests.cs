using System.Net;
using System.Net.Http.Json;
using LazyBean.Application.Ads.Commands.CreateIdentity;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
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

[Collection("Database")]
public class AdsIntegrationTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-ads-user";
    private const string Password = "P@ssw0rd-Ads-Tests!";

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
                    ["Admin:Password"] = "",
                    ["Meta:AccessToken"] = ""
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost")
        });

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        loginRes.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task MetaIntegration_StatusEndpoint_ReturnsMissingWhenUnconfigured()
    {
        var res = await _client.GetAsync("/api/integrations/meta");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<MetaIntegrationStatusDto>();
        Assert.NotNull(body);
        Assert.False(body.Configured);
        Assert.Equal("missing_token", body.State);
    }

    [Fact]
    public async Task AdvertiserIdentities_Lifecycle_CreateApproveRejectAndQuery()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var comp = new Competitor
        {
            Key = $"it-comp-{Guid.NewGuid():N}",
            Name = "Ads Test Competitor",
            BaseUrl = "https://cafelax.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true
        };
        db.Competitors.Add(comp);
        await db.SaveChangesAsync();

        // 1. Create Meta identity
        var createCommand = new CreateIdentityCommand(
            comp.Id,
            "Meta",
            "1049284729102",
            "Cafelax Egypt",
            "https://www.facebook.com/ads/library/?id=1049284729102",
            "https://cafelax.com",
            "Initial setup");

        var createRes = await _client.PostAsJsonAsync("/api/identities", createCommand);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var created = await createRes.Content.ReadFromJsonAsync<AdvertiserIdentityDto>();
        Assert.NotNull(created);
        Assert.Equal(comp.Id, created.CompetitorId);
        Assert.Equal("Meta", created.Platform);
        Assert.Equal("1049284729102", created.ExternalId);
        Assert.Equal("Pending", created.Status);

        // 2. Query identities
        var listRes = await _client.GetAsync($"/api/identities?competitorId={comp.Id}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<IReadOnlyList<AdvertiserIdentityDto>>();
        Assert.NotNull(list);
        Assert.Single(list);

        // 3. Approve identity
        var approveRes = await _client.PostAsJsonAsync($"/api/identities/{created.Id}/approve", new { notes = "Verified official page" });
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        var approved = await approveRes.Content.ReadFromJsonAsync<AdvertiserIdentityDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.ReviewedAt);

        // 4. Reject identity
        var rejectRes = await _client.PostAsJsonAsync($"/api/identities/{created.Id}/reject", new { notes = "Spam entry" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var rejected = await rejectRes.Content.ReadFromJsonAsync<AdvertiserIdentityDto>();
        Assert.NotNull(rejected);
        Assert.Equal("Rejected", rejected.Status);
    }

    [Fact]
    public async Task AdsEndpoint_ReturnsFilteredAdsWithCampaign()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var comp = new Competitor
        {
            Key = $"it-ads-comp-{Guid.NewGuid():N}",
            Name = "Ads Query Competitor",
            BaseUrl = "https://cafelax.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true
        };
        db.Competitors.Add(comp);
        await db.SaveChangesAsync();

        var identity = new AdvertiserIdentity
        {
            CompetitorId = comp.Id,
            Platform = AdPlatform.Google,
            ExternalId = "AR01064852753525768193",
            Name = "CAFELAX Google Ads",
            Status = AdvertiserIdentityStatus.Approved
        };
        db.AdvertiserIdentities.Add(identity);
        await db.SaveChangesAsync();

        var campaign = new AdCampaign
        {
            CompetitorId = comp.Id,
            IdentityId = identity.Id,
            CreativeHash = "a1b2c3d4e5f60718",
            Platform = AdPlatform.Google,
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-5),
            LastSeenAt = DateTimeOffset.UtcNow,
            Status = AdStatus.Active
        };
        db.AdCampaigns.Add(campaign);
        await db.SaveChangesAsync();

        var ad = new Ad
        {
            CompetitorId = comp.Id,
            IdentityId = identity.Id,
            CampaignId = campaign.Id,
            ExternalAdId = "CR11098911077365186561",
            Platform = AdPlatform.Google,
            Title = "De'Longhi Special Promotion",
            Body = "Up to 30% off espresso machines",
            LinkUrl = "https://cafelax.com/collections/machines",
            SnapshotUrl = "https://tpc.googlesyndication.com/archive/simgad/123",
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-5),
            LastSeenAt = DateTimeOffset.UtcNow,
            Status = AdStatus.Active,
            CreativeHash = "a1b2c3d4e5f60718"
        };
        db.Ads.Add(ad);
        await db.SaveChangesAsync();

        var res = await _client.GetAsync($"/api/ads?competitorId={comp.Id}&platform=Google");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<GetAdsResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(1, body.TotalCount);
        Assert.Single(body.Ads);

        var first = body.Ads[0];
        Assert.Equal("CR11098911077365186561", first.ExternalAdId);
        Assert.Equal("Google", first.Platform);
        Assert.Equal("Active", first.Status);
        Assert.Equal("a1b2c3d4e5f60718", first.CreativeHash);
        Assert.Equal("De'Longhi Special Promotion", first.Title);
    }
}
