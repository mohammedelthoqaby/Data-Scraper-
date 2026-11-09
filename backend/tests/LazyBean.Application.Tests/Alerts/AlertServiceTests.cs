using System.Text.Json;
using System.Text.Json.Nodes;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Alerts.Dtos;
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

public class AlertServiceTests
{
    private LazyBeanDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new LazyBeanDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private (AlertService service, LazyBeanDbContext db) CreateAlertService()
    {
        var db = CreateInMemoryDbContext();
        var mockAi = new Mock<IAiClient>();
        var mockMeta = new Mock<IMetaAdLibraryClient>();
        var mockHttp = new Mock<IHttpClientFactory>();
        var mockCache = new Mock<ICacheService>();
        
        mockCache.Setup(c => c.GetOrCreateAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<HashSet<string>>>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<IReadOnlyCollection<string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var service = new AlertService(
            db,
            mockAi.Object,
            mockMeta.Object,
            mockHttp.Object,
            mockCache.Object,
            config,
            NullLogger<AlertService>.Instance);

        return (service, db);
    }

    [Fact]
    public void RulesConfirmSameMatch_WhenAttributesMatch_ReturnsTrue()
    {
        var req = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Caffè Vergnano Antica Bottega Whole Bean Coffee - 500g", "Caffè Vergnano", "Coffee", 500, null, null, null,
            "Caffè Vergnano - Antica Bottega Whole Beans - 500g", "Caffè Vergnano", "Coffee", 500, null, null, null,
            "hash");

        var ok = AiReviewTriageService.RulesConfirmSameMatch(req);
        Assert.True(ok);
    }

    [Fact]
    public void RulesConfirmSameMatch_WhenSizeExceedsTwoPercent_ReturnsFalse()
    {
        // 500g vs 250g
        var req = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Caffè Vergnano Antica Bottega - 500g", "Caffè Vergnano", "Coffee", 500, null, null, null,
            "Caffè Vergnano Antica Bottega - 250g", "Caffè Vergnano", "Coffee", 250, null, null, null,
            "hash");

        var ok = AiReviewTriageService.RulesConfirmSameMatch(req);
        Assert.False(ok);

        // 500g vs 515g (3% diff)
        var req2 = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Coffee A - 500g", "Brand A", "Coffee", 500, null, null, null,
            "Coffee A - 515g", "Brand A", "Coffee", 515, null, null, null,
            "hash2");

        Assert.False(AiReviewTriageService.RulesConfirmSameMatch(req2));

        // 500g vs 505g (1% diff -> allowable)
        var req3 = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Coffee A - 500g", "Brand A", "Coffee", 500, null, null, null,
            "Coffee A - 505g", "Brand A", "Coffee", 505, null, null, null,
            "hash3");

        Assert.True(AiReviewTriageService.RulesConfirmSameMatch(req3));
    }

    [Fact]
    public void RulesConfirmSameMatch_WhenBrandsMismatch_ReturnsFalse()
    {
        var req = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Illy Espresso 250g", "Illy", "Coffee", 250, null, null, null,
            "Lavazza Espresso 250g", "Lavazza", "Coffee", 250, null, null, null,
            "hash");

        var ok = AiReviewTriageService.RulesConfirmSameMatch(req);
        Assert.False(ok);
    }

    [Fact]
    public void RulesConfirmSameMatch_WhenUnitCountDiffers_ReturnsFalse()
    {
        var req = new AiReviewTriagePairRequest(
            Guid.NewGuid(),
            "Vergnano Pods - 15 Pods", "Vergnano", "Coffee", null, 15, "ESE", null,
            "Vergnano Pods - 150 Pods", "Vergnano", "Coffee", null, 150, "ESE", null,
            "hash");

        var ok = AiReviewTriageService.RulesConfirmSameMatch(req);
        Assert.False(ok);
    }

    [Fact]
    public async Task AiReviewTriage_AutoApproves_WhenConfidenceGte95AndRulesMatch()
    {
        using var db = CreateInMemoryDbContext();
        var mockAiClient = new Mock<IAiClient>();

        db.AiSettings.Add(new AiSetting
        {
            IsEnabled = true,
            PrimaryProvider = "groq",
            DailyRequestCap = 500
        });

        var refVarId = Guid.NewGuid();
        var compVarId = Guid.NewGuid();

        var payload = new JsonObject
        {
            ["ReferenceTitle"] = "Caffè Vergnano Antica Bottega 500g",
            ["ReferenceBrand"] = "Caffè Vergnano",
            ["ReferenceCategory"] = "Coffee",
            ["ReferenceWeightGrams"] = 500,
            ["ReferenceVariantId"] = refVarId.ToString(),
            ["CompetitorTitle"] = "Caffè Vergnano Antica Bottega 500g",
            ["CompetitorBrand"] = "Caffè Vergnano",
            ["CompetitorCategory"] = "Coffee",
            ["CompetitorWeightGrams"] = 500,
            ["CompetitorVariantId"] = compVarId.ToString()
        };

        var reviewItem = new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Pending,
            PayloadJson = payload.ToJsonString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewItems.Add(reviewItem);
        await db.SaveChangesAsync();

        mockAiClient.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiReviewTriagePairRequest> reqs, CancellationToken ct) =>
                reqs.Select(r => new AiReviewTriageResult(r.ReviewId, r.PairHash, "same", 0.98m, "Exact brand and roast.")).ToList());

        var service = new AiReviewTriageService(db, mockAiClient.Object, NullLogger<AiReviewTriageService>.Instance);
        var stats = await service.TriagePendingReviewsAsync(10);

        Assert.Equal(1, stats.SameCount);

        // Review item should now be Approved automatically
        var reloadedItem = await db.ReviewItems.FirstAsync(r => r.Id == reviewItem.Id);
        Assert.Equal(ReviewStatus.Approved, reloadedItem.Status);

        // Product match must be created or updated as MatchMethod.AI
        var match = await db.ProductMatches.FirstOrDefaultAsync(m => m.ReferenceVariantId == refVarId && m.CompetitorVariantId == compVarId);
        Assert.NotNull(match);
        Assert.Equal(MatchMethod.AI, match.Method);
        Assert.Equal(MatchStatus.Approved, match.Status);
    }

    [Fact]
    public async Task AiReviewTriage_AutoRejects_WhenVerdictDifferentAndConfidenceGte90()
    {
        using var db = CreateInMemoryDbContext();
        var mockAiClient = new Mock<IAiClient>();

        db.AiSettings.Add(new AiSetting
        {
            IsEnabled = true,
            PrimaryProvider = "groq",
            DailyRequestCap = 500
        });

        var refVarId = Guid.NewGuid();
        var compVarId = Guid.NewGuid();

        var payload = new JsonObject
        {
            ["ReferenceTitle"] = "Caffè Vergnano 15 Pods",
            ["ReferenceBrand"] = "Caffè Vergnano",
            ["ReferenceVariantId"] = refVarId.ToString(),
            ["CompetitorTitle"] = "Caffè Vergnano 150 Pods",
            ["CompetitorBrand"] = "Caffè Vergnano",
            ["CompetitorVariantId"] = compVarId.ToString()
        };

        var reviewItem = new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Pending,
            PayloadJson = payload.ToJsonString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewItems.Add(reviewItem);
        await db.SaveChangesAsync();

        mockAiClient.Setup(c => c.TriageReviewBatchAsync(It.IsAny<IReadOnlyList<AiReviewTriagePairRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiReviewTriagePairRequest> reqs, CancellationToken ct) =>
                reqs.Select(r => new AiReviewTriageResult(r.ReviewId, r.PairHash, "different", 0.95m, "Pack size 15 vs 150")).ToList());

        var service = new AiReviewTriageService(db, mockAiClient.Object, NullLogger<AiReviewTriageService>.Instance);
        await service.TriagePendingReviewsAsync(10);

        var reloadedItem = await db.ReviewItems.FirstAsync(r => r.Id == reviewItem.Id);
        Assert.Equal(ReviewStatus.Rejected, reloadedItem.Status);
    }

    [Fact]
    public async Task PerformActionAsync_HandlesDoneSnoozeAndDismissWithHistory()
    {
        var (service, db) = CreateAlertService();

        var alert = new Alert
        {
            Type = AlertType.LazyBeanMoreExpensiveInStock,
            Severity = AlertSeverity.Urgent,
            Status = AlertStatus.Active,
            Title = "Price gap test",
            Reason = "15% more expensive",
            IdentityKey = "test_alert_1",
            MetricValuesJson = JsonSerializer.Serialize(new { gapPercent = 16.5m })
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        // 1. Snooze for 7 days
        var snoozed = await service.PerformActionAsync(alert.Id, "snooze", null, "admin");
        Assert.Equal("Snoozed", snoozed.Status);
        Assert.NotNull(snoozed.SnoozedUntil);
        Assert.True(snoozed.SnoozedUntil > DateTimeOffset.UtcNow.AddDays(6));

        var histories = await service.GetHistoryAsync(alert.Id);
        Assert.Single(histories);
        Assert.Equal("Snoozed", histories[0].Action);

        // 2. Dismiss with note
        var dismissed = await service.PerformActionAsync(alert.Id, "dismiss", "Competitor is liquidating short-dated stock", "admin");
        Assert.Equal("Dismissed", dismissed.Status);
        Assert.Equal("Competitor is liquidating short-dated stock", dismissed.DismissNote);

        histories = await service.GetHistoryAsync(alert.Id);
        Assert.Equal(2, histories.Count);
        Assert.Equal("Dismissed", histories[0].Action);

        // 3. Mark Done
        var done = await service.PerformActionAsync(alert.Id, "done", null, "admin");
        Assert.Equal("Done", done.Status);
        Assert.NotNull(done.ResolvedAt);
    }

    [Fact]
    public async Task MaterialChange_ReopensSnoozedOrDismissedAlert_WhenGapShiftsByGte5PercentPoints()
    {
        var (service, db) = CreateAlertService();

        // Setup store and products
        var refComp = new Competitor { Id = Guid.NewGuid(), Name = "Lazy Bean", IsReference = true, BaseUrl = "https://lazybean.co" };
        var rivalComp = new Competitor { Id = Guid.NewGuid(), Name = "CAFELAX", IsReference = false, BaseUrl = "https://cafelax.com" };
        db.Competitors.AddRange(refComp, rivalComp);

        var refProd = new Product { Id = Guid.NewGuid(), CompetitorId = refComp.Id, Title = "Lavazza Super Crema 1kg", Status = ProductStatus.Active };
        var compProd = new Product { Id = Guid.NewGuid(), CompetitorId = rivalComp.Id, Title = "Lavazza Super Crema 1kg", Status = ProductStatus.Active };
        db.Products.AddRange(refProd, compProd);

        var refVar = new Variant { Id = Guid.NewGuid(), ProductId = refProd.Id, Title = "1kg", Sku = "SKU1" };
        var compVar = new Variant { Id = Guid.NewGuid(), ProductId = compProd.Id, Title = "1kg", Sku = "SKU2" };
        db.Variants.AddRange(refVar, compVar);

        var match = new ProductMatch
        {
            ReferenceVariantId = refVar.Id,
            CompetitorVariantId = compVar.Id,
            Status = MatchStatus.Approved,
            Score = 1.0m
        };
        db.ProductMatches.Add(match);

        // Seed observation: Ref price 1000 EGP, Comp price 850 EGP (+17.6% gap)
        db.PriceObservations.AddRange(
            new PriceObservation { VariantId = refVar.Id, Price = 1000m, InStock = true, ObservedAt = DateTimeOffset.UtcNow },
            new PriceObservation { VariantId = compVar.Id, Price = 850m, InStock = true, ObservedAt = DateTimeOffset.UtcNow }
        );

        // Pre-seed dismissed alert with old gap 16.0%
        var existingAlert = new Alert
        {
            Type = AlertType.LazyBeanMoreExpensiveInStock,
            Severity = AlertSeverity.Urgent,
            Status = AlertStatus.Dismissed,
            Title = "Lazy Bean 16.0% above rival",
            Reason = "Price gap",
            IdentityKey = $"above_cheapest_{refProd.Id}_{rivalComp.Id}",
            MetricValuesJson = JsonSerializer.Serialize(new { gapPercent = 16.0m })
        };
        db.Alerts.Add(existingAlert);
        await db.SaveChangesAsync();

        // Case 1: Gap moves slightly from 16.0% to 17.6% (delta = 1.6% < 5%) -> should remain Dismissed
        await service.SyncAlertsAsync();
        var reloaded = await db.Alerts.FirstAsync(a => a.IdentityKey == existingAlert.IdentityKey);
        Assert.Equal(AlertStatus.Dismissed, reloaded.Status);

        // Case 2: Competitor drops price to 700 EGP (gap becomes (1000 - 700)/700 = 42.9%, delta = 26.9% >= 5%)
        db.PriceObservations.Add(new PriceObservation { VariantId = compVar.Id, Price = 700m, InStock = true, ObservedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();

        await service.SyncAlertsAsync();
        reloaded = await db.Alerts.FirstAsync(a => a.IdentityKey == existingAlert.IdentityKey);
        Assert.Equal(AlertStatus.Active, reloaded.Status);

        var histories = await service.GetHistoryAsync(reloaded.Id);
        Assert.Contains(histories, h => h.Action == "Reopened");
    }

    [Fact]
    public async Task AlertCalculation_UrgentOutOfStock_TriggersWhenLbOosAndTwoCompetitorsInStock()
    {
        var (service, db) = CreateAlertService();

        var refComp = new Competitor { Id = Guid.NewGuid(), Name = "Lazy Bean", IsReference = true, BaseUrl = "https://lazybean.co" };
        var c1 = new Competitor { Id = Guid.NewGuid(), Name = "CAFELAX", IsReference = false, BaseUrl = "https://cafelax.com" };
        var c2 = new Competitor { Id = Guid.NewGuid(), Name = "Coffee Zone", IsReference = false, BaseUrl = "https://coffeezone.com" };
        db.Competitors.AddRange(refComp, c1, c2);

        var refProd = new Product { Id = Guid.NewGuid(), CompetitorId = refComp.Id, Title = "Caffè Milano 250g", Status = ProductStatus.Active };
        var compProd1 = new Product { Id = Guid.NewGuid(), CompetitorId = c1.Id, Title = "Caffè Milano 250g", Status = ProductStatus.Active };
        var compProd2 = new Product { Id = Guid.NewGuid(), CompetitorId = c2.Id, Title = "Caffè Milano 250g", Status = ProductStatus.Active };
        db.Products.AddRange(refProd, compProd1, compProd2);

        var refVar = new Variant { Id = Guid.NewGuid(), ProductId = refProd.Id, Title = "250g" };
        var v1 = new Variant { Id = Guid.NewGuid(), ProductId = compProd1.Id, Title = "250g" };
        var v2 = new Variant { Id = Guid.NewGuid(), ProductId = compProd2.Id, Title = "250g" };
        db.Variants.AddRange(refVar, v1, v2);

        db.ProductMatches.AddRange(
            new ProductMatch { ReferenceVariantId = refVar.Id, CompetitorVariantId = v1.Id, Status = MatchStatus.Approved, Score = 1.0m },
            new ProductMatch { ReferenceVariantId = refVar.Id, CompetitorVariantId = v2.Id, Status = MatchStatus.Approved, Score = 1.0m }
        );

        // Lazy Bean is OUT OF STOCK; both competitors are IN STOCK
        db.PriceObservations.AddRange(
            new PriceObservation { VariantId = refVar.Id, Price = 300m, InStock = false, ObservedAt = DateTimeOffset.UtcNow },
            new PriceObservation { VariantId = v1.Id, Price = 320m, InStock = true, ObservedAt = DateTimeOffset.UtcNow },
            new PriceObservation { VariantId = v2.Id, Price = 310m, InStock = true, ObservedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        await service.SyncAlertsAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Type == AlertType.LazyBeanOutOfStockMultiCompetitor);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Urgent, alert.Severity);
        Assert.Equal(refProd.Id, alert.ReferenceProductId);
        Assert.Contains("out of stock at Lazy Bean while 2 competitor", alert.Reason);
    }

    [Fact]
    public async Task AlertCalculation_OpportunityAllCompetitorsOutOfStock_TriggersWhenLbInStockAndAllSoldOut()
    {
        var (service, db) = CreateAlertService();

        var refComp = new Competitor { Id = Guid.NewGuid(), Name = "Lazy Bean", IsReference = true, BaseUrl = "https://lazybean.co" };
        var c1 = new Competitor { Id = Guid.NewGuid(), Name = "Fengany", IsReference = false, BaseUrl = "https://fengany.com" };
        db.Competitors.AddRange(refComp, c1);

        var refProd = new Product { Id = Guid.NewGuid(), CompetitorId = refComp.Id, Title = "Exclusive Espresso Roast", Status = ProductStatus.Active };
        var compProd = new Product { Id = Guid.NewGuid(), CompetitorId = c1.Id, Title = "Exclusive Espresso Roast", Status = ProductStatus.Active };
        db.Products.AddRange(refProd, compProd);

        var refVar = new Variant { Id = Guid.NewGuid(), ProductId = refProd.Id, Title = "Default" };
        var compVar = new Variant { Id = Guid.NewGuid(), ProductId = compProd.Id, Title = "Default" };
        db.Variants.AddRange(refVar, compVar);

        db.ProductMatches.Add(new ProductMatch { ReferenceVariantId = refVar.Id, CompetitorVariantId = compVar.Id, Status = MatchStatus.Approved, Score = 1.0m });

        // Lazy Bean is IN STOCK, competitor is OUT OF STOCK
        db.PriceObservations.AddRange(
            new PriceObservation { VariantId = refVar.Id, Price = 450m, InStock = true, ObservedAt = DateTimeOffset.UtcNow },
            new PriceObservation { VariantId = compVar.Id, Price = 420m, InStock = false, ObservedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        await service.SyncAlertsAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Type == AlertType.AllCompetitorsOutOfStock);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Opportunity, alert.Severity);
        Assert.Equal(refProd.Id, alert.ReferenceProductId);
        Assert.Contains("Exclusive inventory", alert.Title);
    }
}
