using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Alerts;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Alerts;

// Our own ads (Lazy Bean) raise their own comparison alert, and never count as competitor campaigns.
public class OwnAdsAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static LazyBeanDbContext NewDb() => new(new DbContextOptionsBuilder<LazyBeanDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static AlertService NewAlertService(LazyBeanDbContext db)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<HashSet<string>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        return new AlertService(
            db,
            new Mock<IAiClient>().Object,
            new Mock<IMetaAdLibraryClient>().Object,
            new Mock<IHttpClientFactory>().Object,
            cache.Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<AlertService>.Instance);
    }

    private sealed record Catalog(Competitor Ours, Competitor Theirs, Product OurProduct, Product TheirProduct);

    // Our product and a competitor's product, matched to each other.
    private static Catalog SeedMatchedCatalog(LazyBeanDbContext db)
    {
        var ours = new Competitor { Key = "lazy-bean", Name = "Lazy Bean", BaseUrl = "https://lazybean.co", Platform = CompetitorPlatform.LazzyBeanApi, IsReference = true };
        var theirs = new Competitor { Key = "coffee-zone", Name = "Coffee Zone Misr", BaseUrl = "https://coffeezonemisr.com", Platform = CompetitorPlatform.Shopify, IsReference = false };
        var ourProduct = new Product { CompetitorId = ours.Id, ExternalId = "our-1", CanonicalUrl = "https://lazybean.co/p/espresso", Title = "Espresso 250g", NormalizedTitle = "espresso 250g", Status = ProductStatus.Active };
        var theirProduct = new Product { CompetitorId = theirs.Id, ExternalId = "their-1", CanonicalUrl = "https://coffeezonemisr.com/p/espresso", Title = "Espresso 250g", NormalizedTitle = "espresso 250g", Status = ProductStatus.Active };
        var ourVariant = new Variant { ProductId = ourProduct.Id, ExternalId = "our-v1", Title = "250g" };
        var theirVariant = new Variant { ProductId = theirProduct.Id, ExternalId = "their-v1", Title = "250g" };

        db.Competitors.AddRange(ours, theirs);
        db.Products.AddRange(ourProduct, theirProduct);
        db.Variants.AddRange(ourVariant, theirVariant);
        db.ProductMatches.Add(new ProductMatch
        {
            CompetitorVariantId = theirVariant.Id,
            ReferenceVariantId = ourVariant.Id,
            Score = 0.95m,
            Method = MatchMethod.Automatic,
            Status = MatchStatus.Auto,
        });
        db.SaveChanges();
        return new Catalog(ours, theirs, ourProduct, theirProduct);
    }

    private static Ad NewAd(Competitor competitor, Guid? productId, string title, string? body = null) => new()
    {
        CompetitorId = competitor.Id,
        IdentityId = Guid.NewGuid(),
        ExternalAdId = Guid.NewGuid().ToString("N"),
        Platform = AdPlatform.Meta,
        Title = title,
        Body = body,
        Status = AdStatus.Active,
        CreativeHash = Guid.NewGuid().ToString("N"),
        FirstSeenAt = Now,
        LastSeenAt = Now,
        ProductId = productId,
    };

    [Fact]
    public async Task OurAdAboveTheCompetitorsAdPrice_RaisesItsOwnAlert()
    {
        using var db = NewDb();
        var catalog = SeedMatchedCatalog(db);
        db.Ads.Add(NewAd(catalog.Ours, catalog.OurProduct.Id, "Espresso 250g 120 EGP"));
        db.Ads.Add(NewAd(catalog.Theirs, catalog.TheirProduct.Id, "Espresso 250g 100 EGP"));
        db.SaveChanges();

        await NewAlertService(db).SyncAlertsAsync();

        var alert = await db.Alerts.SingleAsync(a => a.Type == AlertType.OurAdMoreExpensiveThanCompetitorAd);
        Assert.Equal(catalog.OurProduct.Id, alert.ReferenceProductId);
        Assert.Equal("Coffee Zone Misr", alert.CompetitorName);
        Assert.Equal(20m, alert.EgpImpact);
    }

    [Fact]
    public async Task OurAdAtOrBelowTheCompetitorsAdPrice_RaisesNoOwnAlert()
    {
        using var db = NewDb();
        var catalog = SeedMatchedCatalog(db);
        db.Ads.Add(NewAd(catalog.Ours, catalog.OurProduct.Id, "Espresso 250g 90 EGP"));
        db.Ads.Add(NewAd(catalog.Theirs, catalog.TheirProduct.Id, "Espresso 250g 100 EGP"));
        db.SaveChanges();

        await NewAlertService(db).SyncAlertsAsync();

        Assert.False(await db.Alerts.AnyAsync(a => a.Type == AlertType.OurAdMoreExpensiveThanCompetitorAd));
    }

    [Fact]
    public async Task OwnPromotion_NeverCountsAsACompetitorCampaign()
    {
        using var db = NewDb();
        var catalog = SeedMatchedCatalog(db);
        db.Ads.Add(NewAd(catalog.Ours, catalog.OurProduct.Id, "Espresso 250g", body: "Huge sale 30% off everything"));
        db.SaveChanges();

        await NewAlertService(db).SyncAlertsAsync();

        Assert.False(await db.Alerts.AnyAsync(a => a.Type == AlertType.BigCompetitorCampaign));
    }

    [Fact]
    public async Task CompetitorPromotion_StillRaisesTheCampaignAlert_ForContrast()
    {
        using var db = NewDb();
        var catalog = SeedMatchedCatalog(db);
        db.Ads.Add(NewAd(catalog.Theirs, catalog.TheirProduct.Id, "Espresso 250g", body: "Huge sale 30% off everything"));
        db.SaveChanges();

        await NewAlertService(db).SyncAlertsAsync();

        Assert.True(await db.Alerts.AnyAsync(a => a.Type == AlertType.BigCompetitorCampaign));
    }
}
