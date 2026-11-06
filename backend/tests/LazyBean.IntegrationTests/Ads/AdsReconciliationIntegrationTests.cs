using System.Net;
using System.Net.Http.Json;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Queries.GetAds;
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

// The Ads page's numbers against the database: what the app sent, what is stored, what the page shows, the campaign
// links for Meta and Google, paging of campaigns, and the 30-day rule for Google creatives.
[Collection("Database")]
public class AdsReconciliationIntegrationTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-recon-user";
    private const string Password = "P@ssw0rd-Recon-Tests!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

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

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost"),
        });

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        login.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Summary_ReconcilesIngestedStoredAndDisplayed_AndCampaignsHoldTheirCreatives()
    {
        // A fresh advertiser and page for this test, so the counts are this test's alone.
        var stamp = DateTime.UtcNow.Ticks.ToString();
        var advertiserId = "AR" + stamp.PadLeft(20, '0');
        var pageId = "9" + stamp;
        var competitorId = await CreateCompetitorAsync(stamp);
        await CreateIdentityAsync(competitorId, AdPlatform.Google, advertiserId, "Recon Google");
        await CreateIdentityAsync(competitorId, AdPlatform.Meta, pageId, "Recon Meta");

        var now = DateTimeOffset.UtcNow;
        var google = await IngestGoogleAsync(advertiserId,
            GoogleItem($"CRRECON{stamp}1", "text", "Recon advertiser", now),
            GoogleItem($"CRRECON{stamp}2", "image", "Recon advertiser", now),
            GoogleItem($"CRRECON{stamp}3", "video", "Recon advertiser", now));
        Assert.Equal(3, google.IngestedCount);

        var meta = await IngestMetaAsync(pageId,
            MetaItem($"MRECON{stamp}1", "Same copy for both ads"),
            MetaItem($"MRECON{stamp}2", "Same copy for both ads"));
        Assert.Equal(2, meta.IngestedCount);

        // One Google creative was last shown 40 days ago: it is stored, but not displayed under the default filter.
        await SetLastSeenAsync($"CRRECON{stamp}3", now.AddDays(-40));

        var summary = await _client.GetFromJsonAsync<AdsSummaryDto>("/api/ads/summary");
        Assert.NotNull(summary);

        var googleRow = summary.Rows.Single(r => r.CompetitorId == competitorId && r.Platform == "Google");
        Assert.Equal(1, googleRow.Identities);
        Assert.Equal(3, googleRow.Ingested);
        Assert.Equal(3, googleRow.Stored);
        Assert.Equal(2, googleRow.Displayed);
        Assert.NotNull(googleRow.LastSyncedAt);
        Assert.Null(googleRow.LastError);

        var metaRow = summary.Rows.Single(r => r.CompetitorId == competitorId && r.Platform == "Meta");
        Assert.Equal(2, metaRow.Ingested);
        Assert.Equal(2, metaRow.Stored);
        Assert.Equal(2, metaRow.Displayed);

        // One Google advertiser has one campaign holding its three creatives.
        var googleCampaigns = await _client.GetFromJsonAsync<CampaignPageDto>(
            $"/api/ads/campaigns?competitorId={competitorId}&platform=Google");
        Assert.NotNull(googleCampaigns);
        var googleCampaign = Assert.Single(googleCampaigns.Items);
        Assert.Equal(3, googleCampaign.AdCount);
        Assert.Equal(2, googleCampaign.ActiveAdCount);
        Assert.Equal(3, googleCampaign.Ads.Count);
        Assert.Equal("Recon Google", googleCampaign.IdentityName);

        // The Meta ads are linked to their campaign (they were saved without one before), so the campaign shows both.
        var metaCampaigns = await _client.GetFromJsonAsync<CampaignPageDto>(
            $"/api/ads/campaigns?competitorId={competitorId}&platform=Meta");
        Assert.NotNull(metaCampaigns);
        var metaCampaign = Assert.Single(metaCampaigns.Items);
        Assert.Equal(2, metaCampaign.AdCount);
        Assert.Equal(2, metaCampaign.Ads.Count);

        // Campaigns are paged: one per page, with the real total.
        var firstPage = await _client.GetFromJsonAsync<CampaignPageDto>(
            $"/api/ads/campaigns?competitorId={competitorId}&page=1&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Single(firstPage.Items);
        Assert.Equal(1, firstPage.PageSize);

        // The default list is Active: the stale Google creative is left out.
        var active = await _client.GetFromJsonAsync<GetAdsResponseDto>(
            $"/api/ads?competitorId={competitorId}&status=Active");
        Assert.NotNull(active);
        Assert.Equal(4, active.TotalCount);
    }

    private static GoogleAdPayloadItem GoogleItem(string id, string format, string title, DateTimeOffset seen) => new(
        id,
        format,
        title,
        null,
        $"https://adstransparency.google.com/advertiser/x/creative/{id}?region=EG",
        null,
        seen,
        seen,
        "{}",
        $"https://tpc.googlesyndication.com/archive/simgad/{id}");

    private static MetaAdItemRequest MetaItem(string id, string body) => new(
        id,
        "Recon Meta",
        body,
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        null,
        "Active",
        "{}");

    private async Task<AdsIngestResponse> IngestGoogleAsync(string advertiserId, params GoogleAdPayloadItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var ingest = scope.ServiceProvider.GetRequiredService<IAdsIngestionService>();
        return await ingest.IngestGoogleAdvertiserAsync(new GoogleAdsIngestRequest(advertiserId, "Success", null, null, items));
    }

    private async Task<AdsIngestResponse> IngestMetaAsync(string pageId, params MetaAdItemRequest[] ads)
    {
        using var scope = _factory.Services.CreateScope();
        var ingest = scope.ServiceProvider.GetRequiredService<IAdsIngestionService>();
        return await ingest.IngestMetaPageAsync(new MetaAdsIngestRequest(pageId, "Success", null, ads.ToList()));
    }

    private async Task<Guid> CreateCompetitorAsync(string stamp)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var competitor = new Competitor { Key = $"recon-{stamp}", Name = $"Recon {stamp}" };
        db.Competitors.Add(competitor);
        await db.SaveChangesAsync();
        return competitor.Id;
    }

    private async Task CreateIdentityAsync(Guid competitorId, AdPlatform platform, string externalId, string name)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        db.AdvertiserIdentities.Add(new AdvertiserIdentity
        {
            CompetitorId = competitorId,
            Platform = platform,
            ExternalId = externalId,
            Name = name,
            Status = AdvertiserIdentityStatus.Approved,
        });
        await db.SaveChangesAsync();
    }

    private async Task SetLastSeenAsync(string externalAdId, DateTimeOffset lastSeen)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var ad = await db.Ads.SingleAsync(a => a.ExternalAdId == externalAdId);
        ad.LastSeenAt = lastSeen;
        await db.SaveChangesAsync();
    }
}
