using LazyBean.Collectors.Google;
using LazyBean.Collectors.Http;
using LazyBean.Collectors.Woo;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Ads;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class GoogleAdsSyncServiceTests
{
    private (LazyBeanDbContext db, UnitOfWork uow) CreateContext()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new LazyBeanDbContext(options);
        db.Database.EnsureCreated();
        var uow = new UnitOfWork(db);
        return (db, uow);
    }

    [Fact]
    public async Task SyncGoogleAdsAsync_ResolvesAndCachesAdvertiserId_AndIngestsAds()
    {
        var (db, uow) = CreateContext();

        var competitor = new Competitor
        {
            Id = Guid.NewGuid(),
            Key = "cafelax",
            Name = "Cafelax",
            Platform = CompetitorPlatform.Shopify,
            BaseUrl = "https://cafelax.com",
            Enabled = true
        };
        db.Competitors.Add(competitor);

        var identity = new AdvertiserIdentity
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Google,
            ExternalId = "cafelax.com", // Starts as domain, not AR...
            Name = "CAFELAX",
            Status = AdvertiserIdentityStatus.Approved
        };
        db.AdvertiserIdentities.Add(identity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mockClient = new Mock<IGoogleAdsTransparencyClient>();
        mockClient.Setup(c => c.SearchAdvertiserIdAsync("CAFELAX", It.IsAny<CancellationToken>()))
            .ReturnsAsync("AR01064852753525768193");

        var creative = new GoogleCreativeDto(
            "CR11098911077365186561",
            "AR01064852753525768193",
            "CAFELAX",
            2,
            "image",
            "CAFELAX",
            null,
            null,
            "https://adstransparency.google.com/advertiser/AR01064852753525768193/creative/CR11098911077365186561?region=EG",
            "https://tpc.googlesyndication.com/archive/simgad/12684947687542842870",
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow,
            "{}");

        mockClient.Setup(c => c.SearchCreativesAsync("AR01064852753525768193", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCreativesPageResult([creative], null));

        var service = new GoogleAdsSyncService(
            mockClient.Object,
            db,
            uow,
            NullLogger<GoogleAdsSyncService>.Instance)
        {
            EnableRateLimitingDelays = false
        };

        var result = await service.SyncGoogleAdsAsync(force: true);

        Assert.True(result.Executed);
        Assert.Equal(1, result.AdvertisersProcessed);
        Assert.Equal(1, result.AdsIngested);

        // Verify advertiser ID was resolved and cached in the database
        var updatedIdentity = await db.AdvertiserIdentities.FirstAsync(i => i.Id == identity.Id);
        Assert.Equal("AR01064852753525768193", updatedIdentity.ExternalId);

        // Verify Ad was created
        var ad = await db.Ads.FirstOrDefaultAsync(a => a.ExternalAdId == "CR11098911077365186561");
        Assert.NotNull(ad);
        Assert.Equal(AdPlatform.Google, ad.Platform);
        Assert.Equal(AdStatus.Active, ad.Status);

        // Verify AdCampaign was created
        var campaign = await db.AdCampaigns.FirstOrDefaultAsync(c => c.IdentityId == identity.Id);
        Assert.NotNull(campaign);
        Assert.Equal(AdPlatform.Google, campaign.Platform);
    }

    [Fact]
    public async Task SyncGoogleAdsAsync_BlocksFor24h_WhenRateLimitedOrCaptchaEncountered()
    {
        var (db, uow) = CreateContext();

        var competitor = new Competitor
        {
            Id = Guid.NewGuid(),
            Key = "cafelax",
            Name = "Cafelax",
            Platform = CompetitorPlatform.Shopify,
            BaseUrl = "https://cafelax.com",
            Enabled = true
        };
        db.Competitors.Add(competitor);

        var identity = new AdvertiserIdentity
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Google,
            ExternalId = "AR01064852753525768193",
            Name = "CAFELAX",
            Status = AdvertiserIdentityStatus.Approved
        };
        db.AdvertiserIdentities.Add(identity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mockClient = new Mock<IGoogleAdsTransparencyClient>();
        mockClient.Setup(c => c.SearchCreativesAsync("AR01064852753525768193", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleAdsBlockedException("Rate limited (HTTP 429)."));

        var service = new GoogleAdsSyncService(
            mockClient.Object,
            db,
            uow,
            NullLogger<GoogleAdsSyncService>.Instance)
        {
            EnableRateLimitingDelays = false
        };

        var result = await service.SyncGoogleAdsAsync(force: true);

        Assert.True(result.Executed);
        Assert.Single(result.BlockedAdvertisers);

        // Identity should now be blocked for 24h
        var updatedIdentity = await db.AdvertiserIdentities.FirstAsync(i => i.Id == identity.Id);
        Assert.NotNull(updatedIdentity.BlockedUntil);
        Assert.True(updatedIdentity.BlockedUntil > DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task SyncGoogleAdsIfDueAsync_Skips_WhenLastSyncedWithin7Days()
    {
        var (db, uow) = CreateContext();

        var competitor = new Competitor
        {
            Id = Guid.NewGuid(),
            Key = "cafelax",
            Name = "Cafelax",
            Platform = CompetitorPlatform.Shopify,
            BaseUrl = "https://cafelax.com",
            Enabled = true
        };
        db.Competitors.Add(competitor);

        var identity = new AdvertiserIdentity
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Google,
            ExternalId = "AR01064852753525768193",
            Name = "CAFELAX",
            Status = AdvertiserIdentityStatus.Approved
        };
        db.AdvertiserIdentities.Add(identity);

        // Recent ad synced 2 days ago
        var ad = new Ad
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            IdentityId = identity.Id,
            ExternalAdId = "CR123",
            Platform = AdPlatform.Google,
            Title = "Recent Ad",
            CreativeHash = "hash1",
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastSeenAt = DateTimeOffset.UtcNow.AddDays(-2),
            Status = AdStatus.Active
        };
        db.Ads.Add(ad);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mockClient = new Mock<IGoogleAdsTransparencyClient>();
        var service = new GoogleAdsSyncService(
            mockClient.Object,
            db,
            uow,
            NullLogger<GoogleAdsSyncService>.Instance)
        {
            EnableRateLimitingDelays = false
        };

        var result = await service.SyncGoogleAdsIfDueAsync();

        Assert.NotNull(result);
        Assert.False(result.Executed); // Skipped because under 7 days
        mockClient.Verify(c => c.SearchCreativesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncGoogleAdsIfDueAsync_Executes_WhenLastSyncedMoreThan7DaysAgo()
    {
        var (db, uow) = CreateContext();

        var competitor = new Competitor
        {
            Id = Guid.NewGuid(),
            Key = "cafelax",
            Name = "Cafelax",
            Platform = CompetitorPlatform.Shopify,
            BaseUrl = "https://cafelax.com",
            Enabled = true
        };
        db.Competitors.Add(competitor);

        var identity = new AdvertiserIdentity
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Google,
            ExternalId = "AR01064852753525768193",
            Name = "CAFELAX",
            Status = AdvertiserIdentityStatus.Approved
        };
        db.AdvertiserIdentities.Add(identity);

        // Old ad synced 10 days ago
        var ad = new Ad
        {
            Id = Guid.NewGuid(),
            CompetitorId = competitor.Id,
            IdentityId = identity.Id,
            ExternalAdId = "CR123",
            Platform = AdPlatform.Google,
            Title = "Old Ad",
            CreativeHash = "hash1",
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
            LastSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
            Status = AdStatus.Active
        };
        db.Ads.Add(ad);
        await db.SaveChangesAsync();

        var mockClient = new Mock<IGoogleAdsTransparencyClient>();
        mockClient.Setup(c => c.SearchCreativesAsync("AR01064852753525768193", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleCreativesPageResult([], null));

        var service = new GoogleAdsSyncService(
            mockClient.Object,
            db,
            uow,
            NullLogger<GoogleAdsSyncService>.Instance);

        var result = await service.SyncGoogleAdsIfDueAsync();

        Assert.NotNull(result);
        Assert.True(result.Executed); // Executed because > 7 days
        mockClient.Verify(c => c.SearchCreativesAsync("AR01064852753525768193", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
