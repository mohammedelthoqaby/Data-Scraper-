using System.Net.Http.Json;
using LazyBean.Application.Ads.Common;
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

// The ad window's facts on the gallery detail: the ad's own ID, the advertiser (page) ID of its identity, why an unlinked ad
// has no product, and when its delivery started. They come from the card's own ad, not from the whole group.
[Collection("Database")]
public class AdGalleryDetailTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-ad-detail-user";
    private const string Password = "P@ssw0rd-AdDetail-Tests!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _dashboard = null!;

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

        _dashboard = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await _dashboard.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        login.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync()
    {
        _dashboard.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Detail_carries_the_ad_ids_the_link_reason_and_the_delivery_start()
    {
        var (adId, externalAdId, pageId, deliveryStart) = await SeedAsync();

        var detail = await _dashboard.GetFromJsonAsync<AdGalleryDetailDto>($"/api/ads/gallery/{adId}");

        Assert.NotNull(detail);
        Assert.Equal(adId, detail!.Card.Id);
        Assert.Equal(externalAdId, detail.Facts.ExternalAdId);
        Assert.Equal(pageId, detail.Facts.AdvertiserId);
        Assert.Equal(AdLinkReasons.NoMatch, detail.Facts.LinkReason);
        Assert.Equal(deliveryStart.ToUnixTimeSeconds(), detail.Facts.DeliveryStartAt!.Value.ToUnixTimeSeconds());
        Assert.Null(detail.Facts.DeliveryStopAt);
        Assert.Equal(new[] { "facebook", "instagram" }, detail.Facts.Placements);
    }

    [Fact]
    public async Task Meta_ingest_stores_placements_and_a_collection_without_them_keeps_them()
    {
        var stamp = Guid.NewGuid().ToString("N");
        var pageId = $"PG{stamp}"[..20];
        var competitorId = await CreateStoreAsync(pageId);
        var externalAdId = $"meta-ing-{stamp}"[..24];
        var start = DateTimeOffset.UtcNow.AddDays(-2);

        await IngestMetaAsync(pageId, MetaItem(externalAdId, start, new[] { "FACEBOOK", "INSTAGRAM" }));
        var first = await DetailOfOnlyCardAsync(competitorId);
        Assert.Equal(new[] { "facebook", "instagram" }, first.Facts.Placements);

        // A later collection that reports other placements replaces them.
        await IngestMetaAsync(pageId, MetaItem(externalAdId, start, new[] { "AUDIENCE_NETWORK" }));
        var second = await DetailOfOnlyCardAsync(competitorId);
        Assert.Equal(new[] { "audience_network" }, second.Facts.Placements);

        // A collection that does not report placements keeps what was stored.
        await IngestMetaAsync(pageId, MetaItem(externalAdId, start, null));
        var third = await DetailOfOnlyCardAsync(competitorId);
        Assert.Equal(new[] { "audience_network" }, third.Facts.Placements);
    }

    private static MetaAdItemRequest MetaItem(string id, DateTimeOffset start, IReadOnlyList<string>? placements) =>
        new(id, "Ingest title", "Ingest body", null, null, null, start, null, "Active", null, placements);

    private async Task IngestMetaAsync(string pageId, params MetaAdItemRequest[] ads)
    {
        using var scope = _factory.Services.CreateScope();
        var ingest = scope.ServiceProvider.GetRequiredService<IAdsIngestionService>();
        await ingest.IngestMetaPageAsync(new MetaAdsIngestRequest(pageId, "Success", null, ads.ToList()));
    }

    private async Task<AdGalleryDetailDto> DetailOfOnlyCardAsync(Guid competitorId)
    {
        var gallery = await _dashboard.GetFromJsonAsync<AdGalleryResponseDto>($"/api/ads/gallery?competitorId={competitorId}");
        var card = Assert.Single(gallery!.Items);
        var detail = await _dashboard.GetFromJsonAsync<AdGalleryDetailDto>($"/api/ads/gallery/{card.Id}");
        return detail!;
    }

    private async Task<Guid> CreateStoreAsync(string pageId)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var competitor = new Competitor { Key = $"detail-ing-{pageId}", Name = "Ingest store (integration)" };
        db.Competitors.Add(competitor);
        db.AdvertiserIdentities.Add(new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Meta,
            ExternalId = pageId,
            Name = "Ingest store",
            Status = AdvertiserIdentityStatus.Approved,
        });
        await db.SaveChangesAsync();
        return competitor.Id;
    }

    private async Task<(Guid AdId, string ExternalAdId, string PageId, DateTimeOffset DeliveryStart)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var pageId = $"PG{Guid.NewGuid():N}"[..20];
        var externalAdId = $"meta-ad-{Guid.NewGuid():N}"[..24];
        var deliveryStart = DateTimeOffset.UtcNow.AddDays(-5);
        var competitor = new Competitor { Key = $"detail-it-{Guid.NewGuid():N}", Name = "Detail store (integration)" };
        var identity = new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Meta,
            ExternalId = pageId,
            Name = "Detail store",
            Status = AdvertiserIdentityStatus.Approved,
        };
        var ad = new Ad
        {
            CompetitorId = competitor.Id,
            IdentityId = identity.Id,
            ExternalAdId = externalAdId,
            Platform = AdPlatform.Meta,
            Title = "Detail title",
            Body = "Detail body",
            Status = AdStatus.Active,
            FirstSeenAt = deliveryStart,
            LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
            DeliveryStartDate = deliveryStart,
            LinkReason = AdLinkReasons.NoMatch,
            Placements = "facebook,instagram",
            CreativeHash = externalAdId,
        };

        db.Competitors.Add(competitor);
        db.AdvertiserIdentities.Add(identity);
        db.Ads.Add(ad);
        await db.SaveChangesAsync();
        return (ad.Id, externalAdId, pageId, deliveryStart);
    }
}
