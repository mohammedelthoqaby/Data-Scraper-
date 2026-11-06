using LazyBean.Application.Ads.Commands.AddIdentityFromUrl;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

// Our own Meta page (Lazy Bean) is seeded as Pending, and a Facebook link adds a page for approval.
public class ReferenceIdentityTests
{
    private const string LazyBeanPageId = "433792133147901";

    private static ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        // The seed reads seed/competitors.json from the repository, so point the content root at it.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "seed", "competitors.json")))
        {
            current = current.Parent;
        }

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(current?.FullName ?? AppContext.BaseDirectory);
        services.AddSingleton(env.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seed_ListsTheReferencePageAsPending_AndIsIdempotent()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());

        await DbInitializer.SeedCompetitorsAsync(sp);
        await DbInitializer.SeedCompetitorsAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var lazyBean = await db.Competitors.SingleAsync(c => c.Key == "lazy-bean");
        var identity = Assert.Single(await db.AdvertiserIdentities.Where(i => i.ExternalId == LazyBeanPageId).ToListAsync());
        Assert.Equal(AdvertiserIdentityStatus.Pending, identity.Status);
        Assert.Equal(AdPlatform.Meta, identity.Platform);
        Assert.Equal(lazyBean.Id, identity.CompetitorId);
    }

    [Fact]
    public async Task Seed_DoesNotChangeAnIdentityAnAdminAlreadyApproved()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        await DbInitializer.SeedCompetitorsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var identity = await db.AdvertiserIdentities.SingleAsync(i => i.ExternalId == LazyBeanPageId);
            identity.Approve("Approved by the admin");
            await db.SaveChangesAsync();
        }

        await DbInitializer.SeedCompetitorsAsync(sp);

        using var check = sp.CreateScope();
        var after = await check.ServiceProvider.GetRequiredService<LazyBeanDbContext>()
            .AdvertiserIdentities.SingleAsync(i => i.ExternalId == LazyBeanPageId);
        Assert.Equal(AdvertiserIdentityStatus.Approved, after.Status);
    }

    [Fact]
    public async Task FacebookLinkWithAPageId_ListsThePageAsPending()
    {
        var (db, handler, _, competitorId) = Setup(connected: true);

        var result = await handler.Handle(
            new AddIdentityFromUrlCommand(competitorId, "https://www.facebook.com/profile.php?id=61565792458339"),
            CancellationToken.None);

        Assert.Equal("pending", result.Status);
        Assert.Equal("61565792458339", result.PageId);
        var identity = await db.AdvertiserIdentities.SingleAsync(i => i.ExternalId == "61565792458339");
        Assert.Equal(AdvertiserIdentityStatus.Pending, identity.Status);
        Assert.Equal(competitorId, identity.CompetitorId);
    }

    [Fact]
    public async Task FacebookLinkForAPageAlreadyListed_IsReportedAsExisting()
    {
        var (db, handler, _, competitorId) = Setup(connected: true);
        var command = new AddIdentityFromUrlCommand(competitorId, "https://www.facebook.com/profile.php?id=61565792458339");
        await handler.Handle(command, CancellationToken.None);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("exists", result.Status);
        Assert.Equal(1, await db.AdvertiserIdentities.CountAsync(i => i.ExternalId == "61565792458339"));
    }

    [Fact]
    public async Task FacebookPageName_IsSentToTheDesktopApp_WhenItIsConnected()
    {
        var (_, handler, jobs, competitorId) = Setup(connected: true);

        var result = await handler.Handle(
            new AddIdentityFromUrlCommand(competitorId, "https://www.facebook.com/lazybeanonline"),
            CancellationToken.None);

        Assert.Equal("resolving", result.Status);
        Assert.NotNull(result.JobId);
        jobs.Verify(j => j.PlanResolveMetaPageAsync(competitorId, "lazybeanonline", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FacebookPageName_SaysItWaits_WhenNoAppIsConnected()
    {
        var (_, handler, _, competitorId) = Setup(connected: false);

        var result = await handler.Handle(
            new AddIdentityFromUrlCommand(competitorId, "https://www.facebook.com/lazybeanonline"),
            CancellationToken.None);

        Assert.Equal("waiting_for_app", result.Status);
        Assert.NotNull(result.JobId);
    }

    [Fact]
    public async Task NotAFacebookLink_IsRejected()
    {
        var (_, handler, _, competitorId) = Setup(connected: true);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AddIdentityFromUrlCommand(competitorId, "https://example.com/lazybeanonline"), CancellationToken.None));
    }

    private static (LazyBeanDbContext Db, AddIdentityFromUrlCommandHandler Handler, Mock<ICollectorJobService> Jobs, Guid CompetitorId) Setup(bool connected)
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LazyBeanDbContext(options);
        var lazyBean = new Competitor
        {
            Key = "lazy-bean",
            Name = "Lazy Bean",
            BaseUrl = "https://lazybean.co",
            Platform = CompetitorPlatform.LazzyBeanApi,
            IsReference = true,
        };
        db.Competitors.Add(lazyBean);
        db.SaveChanges();

        var jobs = new Mock<ICollectorJobService>();
        jobs.Setup(j => j.IsCollectorConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connected);
        jobs.Setup(j => j.PlanResolveMetaPageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var handler = new AddIdentityFromUrlCommandHandler(new UnitOfWork(db), db, jobs.Object);
        return (db, handler, jobs, lazyBean.Id);
    }
}
