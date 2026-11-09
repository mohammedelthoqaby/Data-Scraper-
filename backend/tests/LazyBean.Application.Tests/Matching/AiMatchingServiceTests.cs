using System.Net;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching;
using LazyBean.Application.Matching.AiMatching;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Ai.RateLimiting;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static LazyBean.Application.Tests.Ai.AiTestSupport;

namespace LazyBean.Application.Tests.Matching;

/// <summary>
/// End-to-end AI matching on a small in-memory catalog: cache, confirmation, review queue,
/// manual protection, malformed replies, quota stops, and the Groq-to-Gemini fallback.
/// </summary>
public class AiMatchingServiceTests
{
    private const string RefTitle = "Lavazza Qualita Oro Beans 250g";
    private const string CompTitle = "Lavazza Qualita Rossa Beans 250g";

    private sealed class Fixture
    {
        public required LazyBeanDbContext Db { get; init; }
        public required Guid CompetitorVariantId { get; init; }
        public required Guid ReferenceVariantId { get; init; }
    }

    private static async Task<Fixture> SeedPairAsync(
        LazyBeanDbContext db,
        string referenceTitle = RefTitle,
        string competitorTitle = CompTitle)
    {
        var reference = new Competitor { Key = "lazybean", Name = "Lazy Bean", BaseUrl = "https://lazybean.example", IsReference = true, Enabled = true };
        var rival = new Competitor { Key = "rival", Name = "Rival Coffee", BaseUrl = "https://rival.example", IsReference = false, Enabled = true };
        db.Competitors.AddRange(reference, rival);

        db.AiSettings.Add(new AiSetting { IsEnabled = true, PrimaryProvider = "groq", DailyRequestCap = 1000, TriageRunCap = 200 });

        var refProduct = new Product
        {
            CompetitorId = reference.Id,
            ExternalId = "ref-1",
            CanonicalUrl = "https://lazybean.example/products/lavazza-oro-250g",
            Title = referenceTitle,
            NormalizedTitle = referenceTitle.ToLowerInvariant(),
            Brand = "Lavazza",
            Vendor = "Lavazza",
            CanonicalCategory = CanonicalCategory.CoffeeBeans,
            Status = ProductStatus.Active,
            WeightGrams = 250,
            Handle = "lavazza-oro-250g",
        };
        var refVariant = new Variant { ProductId = refProduct.Id, ExternalId = "ref-1-v", WeightGrams = 250 };

        var compProduct = new Product
        {
            CompetitorId = rival.Id,
            ExternalId = "rival-1",
            CanonicalUrl = "https://rival.example/products/lavazza-rossa-250g",
            Title = competitorTitle,
            NormalizedTitle = competitorTitle.ToLowerInvariant(),
            Brand = "Lavazza",
            Vendor = "Lavazza",
            CanonicalCategory = CanonicalCategory.CoffeeBeans,
            Status = ProductStatus.Active,
            WeightGrams = 250,
            Handle = "lavazza-rossa-250g",
        };
        var compVariant = new Variant { ProductId = compProduct.Id, ExternalId = "rival-1-v", WeightGrams = 250 };

        db.Products.AddRange(refProduct, compProduct);
        db.Variants.AddRange(refVariant, compVariant);
        db.PriceObservations.AddRange(
            new PriceObservation { VariantId = refVariant.Id, RunId = Guid.NewGuid(), Price = 300m },
            new PriceObservation { VariantId = compVariant.Id, RunId = Guid.NewGuid(), Price = 280m });

        await db.SaveChangesAsync();
        return new Fixture { Db = db, CompetitorVariantId = compVariant.Id, ReferenceVariantId = refVariant.Id };
    }

    private static AiMatchingService CreateService(LazyBeanDbContext db, IAiClient ai)
    {
        var cache = new Mock<ICacheService>();
        var matcher = new ProductMatcher(new UnitOfWork(db), db, cache.Object, NullLogger<ProductMatcher>.Instance);
        return new AiMatchingService(new UnitOfWork(db), db, matcher, ai, NullLogger<AiMatchingService>.Instance);
    }

    private static Mock<IAiClient> ReplyWith(string text, string provider = "groq") =>
        ReplyWithCompletion(new AiCompletionResult(provider, "openai/gpt-oss-120b", text, 120));

    private static Mock<IAiClient> ReplyWithCompletion(AiCompletionResult completion)
    {
        var mock = new Mock<IAiClient>();
        mock.Setup(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completion);
        return mock;
    }

    private static string Verdict(string verdict, double confidence, string reason = "reason") =>
        "{\"results\":[{\"pair\":1,\"verdict\":\"" + verdict + "\",\"confidence\":" +
        confidence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"reason\":\"" + reason + "\"}]}";

    [Fact]
    public async Task CacheHit_AvoidsASecondAiCall()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var ai = ReplyWith(Verdict("different", 0.9, "different product line"));
        var service = CreateService(fixture.Db, ai.Object);

        var first = await service.RunAsync(null);
        var second = await service.RunAsync(null);

        Assert.Equal(1, first.SentToAi);
        Assert.Equal(0, second.SentToAi);
        Assert.Equal(1, second.CacheHits);
        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, await fixture.Db.AiMatchDecisions.CountAsync());
    }

    [Fact]
    public async Task Confident_Same_BecomesAnAiConfirmedMatch()
    {
        var fixture = await SeedPairAsync(CreateDb(), referenceTitle: "Lavazza Qualita Oro 250g", competitorTitle: "Lavazza Qualita Oro Whole Beans 250g");
        var service = CreateService(fixture.Db, ReplyWith(Verdict("same", 0.92, "same line, beans and 250g")).Object);

        var stats = await service.RunAsync(null);

        Assert.Equal(1, stats.Confirmed);
        var match = await fixture.Db.ProductMatches.AsNoTracking().SingleAsync();
        Assert.Equal(MatchMethod.AI, match.Method);
        Assert.Equal(MatchStatus.Auto, match.Status);
        Assert.Equal("same line, beans and 250g", match.Reason);
        Assert.False(match.IsBundle);
    }

    [Fact]
    public async Task UnsureVerdict_GoesToReviewQueueWithAiSuggestion()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var service = CreateService(fixture.Db, ReplyWith(Verdict("unsure", 0.5, "packaging differs")).Object);

        var stats = await service.RunAsync(null);

        Assert.Equal(1, stats.ToReview);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
        var review = await fixture.Db.ReviewItems.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Contains("\"AiVerdict\":\"unsure\"", review.PayloadJson);
        Assert.Contains("\"AiSource\":\"missed\"", review.PayloadJson);
        Assert.Contains("packaging differs", review.PayloadJson);
    }

    [Fact]
    public async Task DifferentVerdict_IsStoredAsRejectedAndNotShown()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var service = CreateService(fixture.Db, ReplyWith(Verdict("different", 0.95, "different roast line")).Object);

        var stats = await service.RunAsync(null);

        Assert.Equal(1, stats.Rejected);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.ReviewItems.AsNoTracking().ToListAsync());
        Assert.Equal("different", (await fixture.Db.AiMatchDecisions.AsNoTracking().SingleAsync()).Verdict);
    }

    [Fact]
    public async Task GuardrailFailure_OnAnUncertainRuleMatch_IsRejectedWithoutAnAiCall()
    {
        // Beans versus ground is excluded by blocking for missed pairs, so this case comes from an
        // uncertain rule review: the guardrail must still reject it before the AI is asked.
        var fixture = await SeedPairAsync(CreateDb(), referenceTitle: "Lavazza Qualita Oro Beans 250g", competitorTitle: "Lavazza Qualita Oro Ground 250g");
        fixture.Db.ReviewItems.Add(new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Pending,
            PayloadJson = "{\"CompetitorVariantId\":\"" + fixture.CompetitorVariantId + "\",\"ReferenceVariantId\":\"" + fixture.ReferenceVariantId +
                          "\",\"Score\":0.75,\"CanonicalCategory\":\"CoffeeBeans\"}",
        });
        await fixture.Db.SaveChangesAsync();

        var ai = ReplyWith(Verdict("same", 0.99));
        var service = CreateService(fixture.Db, ai.Object);

        var stats = await service.RunAsync(null);

        Assert.Equal(1, stats.GuardrailBlocked);
        Assert.Equal(0, stats.SentToAi);
        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
        Assert.Equal(ReviewStatus.Rejected, (await fixture.Db.ReviewItems.AsNoTracking().SingleAsync()).Status);
        Assert.Contains("different form", (await fixture.Db.AiMatchDecisions.AsNoTracking().SingleAsync()).GuardrailReason);
    }

    [Fact]
    public async Task MalformedReply_StoresNothingAndIsRetriedNextRun()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var ai = ReplyWith("Sure, these look the same to me!");
        var service = CreateService(fixture.Db, ai.Object);

        var stats = await service.RunAsync(null);

        Assert.Equal(1, stats.Malformed);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.ReviewItems.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.AiMatchDecisions.AsNoTracking().ToListAsync());

        // Nothing was cached, so the next run asks again.
        await service.RunAsync(null);
        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ManualRejection_IsNeverOverridden()
    {
        var fixture = await SeedPairAsync(CreateDb(), referenceTitle: "Lavazza Qualita Oro 250g", competitorTitle: "Lavazza Qualita Oro Whole Beans 250g");
        fixture.Db.ProductMatches.Add(new ProductMatch
        {
            CompetitorVariantId = fixture.CompetitorVariantId,
            ReferenceVariantId = fixture.ReferenceVariantId,
            Score = 0.9m,
            Method = MatchMethod.Manual,
            Status = MatchStatus.Rejected,
        });
        await fixture.Db.SaveChangesAsync();

        var ai = ReplyWith(Verdict("same", 0.99));
        var service = CreateService(fixture.Db, ai.Object);

        await service.RunAsync(null);

        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var match = await fixture.Db.ProductMatches.AsNoTracking().SingleAsync();
        Assert.Equal(MatchMethod.Manual, match.Method);
        Assert.Equal(MatchStatus.Rejected, match.Status);
    }

    [Fact]
    public async Task ManualReviewDecision_IsNeverReopenedOrOverridden()
    {
        var fixture = await SeedPairAsync(CreateDb());
        fixture.Db.ReviewItems.Add(new ReviewItem
        {
            Kind = ReviewItemKind.ProductMatch,
            Status = ReviewStatus.Rejected,
            PayloadJson = "{\"CompetitorVariantId\":\"" + fixture.CompetitorVariantId + "\",\"ReferenceVariantId\":\"" + fixture.ReferenceVariantId + "\",\"Score\":0.7}",
        });
        await fixture.Db.SaveChangesAsync();

        var ai = ReplyWith(Verdict("same", 0.99));
        var service = CreateService(fixture.Db, ai.Object);

        await service.RunAsync(null);

        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var review = await fixture.Db.ReviewItems.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewStatus.Rejected, review.Status);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task QuotaReached_StopsGracefullyAndReportsItemsLeftForNextRun()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var ai = new Mock<IAiClient>();
        ai.Setup(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiUnavailableException("AI quota reached", quotaReached: true));
        var service = CreateService(fixture.Db, ai.Object);

        var stats = await service.RunAsync(null);

        Assert.Equal("AI quota reached", stats.StopReason);
        Assert.Equal(1, stats.SkippedQuota);
        Assert.StartsWith("AI quota reached, 1 items left for next run.", stats.Summary);
        Assert.Empty(await fixture.Db.ProductMatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AiDisabled_DoesNothing()
    {
        var fixture = await SeedPairAsync(CreateDb());
        var setting = await fixture.Db.AiSettings.SingleAsync();
        setting.IsEnabled = false;
        await fixture.Db.SaveChangesAsync();

        var ai = ReplyWith(Verdict("same", 0.99));
        var stats = await CreateService(fixture.Db, ai.Object).RunAsync(null);

        Assert.Equal("AI assistance is disabled", stats.Summary);
        ai.Verify(c => c.CompleteJsonAsync(It.IsAny<AiJsonRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GroqRateLimited_FallsBackToGemini_AndTheDecisionRecordsGemini()
    {
        var db = CreateDb();
        var fixture = await SeedPairAsync(db, referenceTitle: "Lavazza Qualita Oro 250g", competitorTitle: "Lavazza Qualita Oro Whole Beans 250g");

        var groq = new FakeProvider("groq", () => throw new AiProviderException(
            AiProviderErrorKind.RateLimited, "groq rate limit or quota reached (HTTP 429).", 429, TimeSpan.FromSeconds(30)));
        var gemini = new FakeProvider("gemini", () => new AiProviderResponse(Verdict("same", 0.91, "same product"), 90, null));
        var router = new AiClient(
            db,
            CreateProtector(),
            [groq, gemini],
            new AiRateGate(CreateOptions()),
            CreateOptions(),
            NullLogger<AiClient>.Instance);

        var stats = await CreateService(db, router).RunAsync(null);

        Assert.Equal(1, stats.Confirmed);
        Assert.Equal(1, groq.CallCount);
        Assert.Equal(1, gemini.CallCount);
        Assert.Equal("gemini", (await db.AiMatchDecisions.AsNoTracking().SingleAsync()).Provider);
        Assert.Equal(MatchMethod.AI, (await db.ProductMatches.AsNoTracking().SingleAsync()).Method);

        var groqSettings = await db.AiProviderSettings.AsNoTracking().SingleAsync(p => p.Provider == "groq");
        Assert.NotNull(groqSettings.Last429At);
    }

    private static LazyBeanDbContext CreateDb() => LazyBean.Application.Tests.Ai.AiTestSupport.CreateDb();
}
