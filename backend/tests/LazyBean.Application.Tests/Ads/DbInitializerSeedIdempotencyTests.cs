using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ads;

public class DbInitializerSeedIdempotencyTests
{
    private ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        var envMock = new Mock<IHostEnvironment>();
        // Set ContentRootPath so it points to the repository root where seed/competitors.json lives
        var baseDir = AppContext.BaseDirectory;
        // Search up to find seed/competitors.json
        var current = new DirectoryInfo(baseDir);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "seed", "competitors.json")))
        {
            current = current.Parent;
        }

        envMock.Setup(e => e.ContentRootPath).Returns(current?.FullName ?? baseDir);
        services.AddSingleton(envMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedCompetitorsAsync_IsIdempotent_AndPreservesAdminDecisions()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);

        // First seed: Meta advertiser identities are auto-approved at startup
        await DbInitializer.SeedCompetitorsAsync(sp);

        Guid rejectedId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var competitors = await db.Competitors.ToListAsync();
            Assert.NotEmpty(competitors);

            var metaIdentities = await db.AdvertiserIdentities
                .Where(i => i.Platform == AdPlatform.Meta)
                .ToListAsync();

            // 5 competitors have approved Meta IDs in seed/competitors.json; our own page is seeded as Pending.
            Assert.Equal(6, metaIdentities.Count);
            Assert.Equal(5, metaIdentities.Count(i => i.Status == AdvertiserIdentityStatus.Approved));
            Assert.Equal(1, metaIdentities.Count(i => i.Status == AdvertiserIdentityStatus.Pending));

            // Simulate admin rejecting one identity
            var rejected = metaIdentities.First(i => i.Status == AdvertiserIdentityStatus.Approved);
            rejected.Status = AdvertiserIdentityStatus.Rejected;
            rejected.ReviewedAt = DateTimeOffset.UtcNow;
            rejectedId = rejected.Id;
            await db.SaveChangesAsync();
        }

        // Second seed: must not duplicate identities, and must not undo the admin's rejection
        await DbInitializer.SeedCompetitorsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var metaIdentities = await db.AdvertiserIdentities
                .Where(i => i.Platform == AdPlatform.Meta)
                .ToListAsync();

            // Still exactly 6 identities (no duplicates)
            Assert.Equal(6, metaIdentities.Count);

            var rejected = metaIdentities.Single(i => i.Id == rejectedId);
            Assert.Equal(AdvertiserIdentityStatus.Rejected, rejected.Status);
            Assert.NotNull(rejected.ReviewedAt);

            Assert.Equal(4, metaIdentities.Count(i => i.Status == AdvertiserIdentityStatus.Approved));
        }
    }
}
