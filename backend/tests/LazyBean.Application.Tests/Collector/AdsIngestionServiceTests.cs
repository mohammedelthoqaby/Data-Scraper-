using LazyBean.Application.Ads.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Infrastructure.Ads;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Collector;

// The desktop app and the internal API share this ingest path, so a replayed batch must not
// duplicate ads, and status handling must match what the server-side sync always did.
public class AdsIngestionServiceTests
{
    private const string PageId = "102631558265863";

    private static LazyBeanDbContext NewDbWithApprovedMetaIdentity(out AdvertiserIdentity identity)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LazyBeanDbContext(options);

        var competitor = new Competitor
        {
            Key = "strong-coffee",
            Name = "Strong Coffee",
            BaseUrl = "https://strongcoffee-eg.com",
            Platform = CompetitorPlatform.WooCommerce,
            Enabled = true,
            CollectionHost = "desktop",
        };
        db.Competitors.Add(competitor);

        identity = new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = AdPlatform.Meta,
            ExternalId = PageId,
            Name = "Strong Coffee",
            Status = AdvertiserIdentityStatus.Approved,
        };
        db.AdvertiserIdentities.Add(identity);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static AdsIngestionService NewService(LazyBeanDbContext db) =>
        new(new UnitOfWork(db), db, NullLogger<AdsIngestionService>.Instance);

    private static MetaAdsIngestRequest Batch(string status = "Success") => new(
        PageId,
        status,
        null,
        [
            new MetaAdItemRequest("A1", "Title one", "عرض خاص 250 جنيه", "https://strongcoffee-eg.com/p/one", null, null,
                DateTimeOffset.UtcNow.AddDays(-1), null, "Active", null),
            new MetaAdItemRequest("A2", null, "Body two", null, null, null, null, null, "Active", null),
        ]);

    [Fact]
    public async Task MetaIngest_ReplayedBatch_AddsNothingTheSecondTime()
    {
        await using var db = NewDbWithApprovedMetaIdentity(out _);

        var first = await NewService(db).IngestMetaPageAsync(Batch());
        db.ChangeTracker.Clear();
        var second = await NewService(db).IngestMetaPageAsync(Batch());

        Assert.Equal("Success", first.Status);
        Assert.Equal(2, first.IngestedCount);
        Assert.Equal("Success", second.Status);
        Assert.Equal(0, second.IngestedCount);
        Assert.Equal(2, await db.Ads.CountAsync());
    }

    [Fact]
    public async Task MetaIngest_UnknownPage_ThrowsNotFound()
    {
        await using var db = NewDbWithApprovedMetaIdentity(out _);
        var request = Batch() with { PageId = "999" };

        await Assert.ThrowsAsync<NotFoundException>(() => NewService(db).IngestMetaPageAsync(request));
    }

    [Fact]
    public async Task MetaIngest_FailedPage_ReportsFailedAndLeavesAdsAlone()
    {
        await using var db = NewDbWithApprovedMetaIdentity(out _);

        var response = await NewService(db).IngestMetaPageAsync(Batch(status: "Failed") with { Error = "GraphQL request failed" });

        Assert.Equal("Failed", response.Status);
        Assert.Equal("GraphQL request failed", response.Message);
        Assert.Equal(0, await db.Ads.CountAsync());
    }

    [Fact]
    public async Task MetaIngest_BlockedPage_BlocksTheIdentityForTwentyFourHours()
    {
        await using var db = NewDbWithApprovedMetaIdentity(out _);
        var before = DateTimeOffset.UtcNow;

        var response = await NewService(db).IngestMetaPageAsync(Batch(status: "Blocked") with { Error = "Rate limit" });

        Assert.Equal("Blocked", response.Status);
        var identity = await db.AdvertiserIdentities.SingleAsync();
        Assert.NotNull(identity.BlockedUntil);
        Assert.InRange(identity.BlockedUntil!.Value, before.AddHours(23), before.AddHours(25));
        Assert.Equal(0, await db.Ads.CountAsync());
    }
}
