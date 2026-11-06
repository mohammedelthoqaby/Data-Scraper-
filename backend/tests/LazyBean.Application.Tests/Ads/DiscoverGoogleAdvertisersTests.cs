using LazyBean.Application.Ads.Commands.DiscoverGoogleAdvertisers;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class DiscoverGoogleAdvertisersTests
{
    private static async Task<(LazyBeanDbContext Db, Guid CompetitorId, DiscoverGoogleAdvertisersCommandHandler Handler)> ArrangeAsync(
        IGoogleAdsTransparencyClient googleClient)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LazyBeanDbContext(options);

        var competitorId = Guid.NewGuid();
        db.Competitors.Add(new Competitor
        {
            Id = competitorId,
            Key = "cafelax",
            Name = "CAFELAX",
            BaseUrl = "https://cafelax.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var handler = new DiscoverGoogleAdvertisersCommandHandler(
            new UnitOfWork(db),
            db,
            googleClient,
            NullLogger<DiscoverGoogleAdvertisersCommandHandler>.Instance);

        return (db, competitorId, handler);
    }

    [Fact]
    public async Task Handle_DiscoversCandidates_AndStoresAsPendingIdempotently()
    {
        var mockGoogleClient = new Mock<IGoogleAdsTransparencyClient>();
        mockGoogleClient.Setup(c => c.SearchCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdvertiserCandidateDto>
            {
                new("AR01064852753525768193", "CAFELAX", "EG", 52)
            });

        var (db, competitorId, handler) = await ArrangeAsync(mockGoogleClient.Object);
        await using var _ = db;

        var result1 = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Equal("found", result1.Status);
        Assert.Equal(1, result1.CandidatesFound);
        Assert.Null(result1.Message);
        Assert.Single(result1.Identities);
        Assert.Equal("AR01064852753525768193", result1.Identities[0].ExternalId);
        Assert.Equal("Pending", result1.Identities[0].Status);
        Assert.Equal("Google", result1.Identities[0].Platform);

        var inDb1 = await db.AdvertiserIdentities.Where(i => i.CompetitorId == competitorId).ToListAsync();
        Assert.Single(inDb1);
        Assert.Equal(AdvertiserIdentityStatus.Pending, inDb1[0].Status);

        // Second execution: must be idempotent and not create duplicate records.
        var result2 = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Equal(1, result2.CandidatesFound);
        Assert.Single(result2.Identities);
        var inDb2 = await db.AdvertiserIdentities.Where(i => i.CompetitorId == competitorId).ToListAsync();
        Assert.Single(inDb2);
    }

    [Fact]
    public async Task Handle_GoogleReturnsNoAdvertisers_ReportsNoneWithNoGoogleAdvertiserMessage()
    {
        var mockGoogleClient = new Mock<IGoogleAdsTransparencyClient>();
        mockGoogleClient.Setup(c => c.SearchCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdvertiserCandidateDto>());

        var (db, competitorId, handler) = await ArrangeAsync(mockGoogleClient.Object);
        await using var _ = db;

        var result = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Equal("none", result.Status);
        Assert.Equal(0, result.CandidatesFound);
        Assert.Equal("No Google advertiser found for CAFELAX.", result.Message);
        Assert.Empty(result.Identities);
    }

    [Fact]
    public async Task Handle_GoogleBlocksRequest_ReportsBlockedNotNone()
    {
        var mockGoogleClient = new Mock<IGoogleAdsTransparencyClient>();
        mockGoogleClient.Setup(c => c.SearchCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleAdsBlockedException("Google Ads Transparency rate limited requests (HTTP 429)."));

        var (db, competitorId, handler) = await ArrangeAsync(mockGoogleClient.Object);
        await using var _ = db;

        var result = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Equal("blocked", result.Status);
        Assert.Contains("HTTP 429", result.Message);
        Assert.DoesNotContain("No Google advertiser found", result.Message ?? string.Empty);
        Assert.Empty(result.Identities);
    }
}
