using LazyBean.Application.Ads.Commands.DiscoverGoogleAdvertisers;
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
    [Fact]
    public async Task Handle_DiscoversCandidates_AndStoresAsPendingIdempotently()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var db = new LazyBeanDbContext(options);

        var competitorId = Guid.NewGuid();
        var competitor = new Competitor
        {
            Id = competitorId,
            Key = "cafelax",
            Name = "CAFELAX",
            BaseUrl = "https://cafelax.com",
            Platform = CompetitorPlatform.Shopify,
            Enabled = true
        };
        db.Competitors.Add(competitor);
        await db.SaveChangesAsync();

        var mockGoogleClient = new Mock<IGoogleAdsTransparencyClient>();
        mockGoogleClient.Setup(c => c.SearchCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdvertiserCandidateDto>
            {
                new("AR01064852753525768193", "CAFELAX", "EG", 52)
            });

        var uow = new UnitOfWork(db);
        var handler = new DiscoverGoogleAdvertisersCommandHandler(
            uow,
            db,
            mockGoogleClient.Object,
            NullLogger<DiscoverGoogleAdvertisersCommandHandler>.Instance);

        // First execution
        var result1 = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Single(result1);
        Assert.Equal("AR01064852753525768193", result1[0].ExternalId);
        Assert.Equal("Pending", result1[0].Status);
        Assert.Equal("Google", result1[0].Platform);

        var inDb1 = await db.AdvertiserIdentities.Where(i => i.CompetitorId == competitorId).ToListAsync();
        Assert.Single(inDb1);
        Assert.Equal(AdvertiserIdentityStatus.Pending, inDb1[0].Status);

        // Second execution: must be idempotent and not create duplicate records
        var result2 = await handler.Handle(new DiscoverGoogleAdvertisersCommand(competitorId), CancellationToken.None);

        Assert.Single(result2);
        var inDb2 = await db.AdvertiserIdentities.Where(i => i.CompetitorId == competitorId).ToListAsync();
        Assert.Single(inDb2);
    }
}
