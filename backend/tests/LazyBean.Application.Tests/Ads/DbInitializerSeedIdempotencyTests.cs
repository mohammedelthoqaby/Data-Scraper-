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
    public async Task SeedCompetitorsAsync_IsIdempotent_AndPreservesApprovedStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);

        // First Seed: should seed competitors and Meta advertiser identities as Pending
        await DbInitializer.SeedCompetitorsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var competitors = await db.Competitors.ToListAsync();
            Assert.NotEmpty(competitors);

            var metaIdentities = await db.AdvertiserIdentities
                .Where(i => i.Platform == AdPlatform.Meta)
                .ToListAsync();

            // We have 5 competitors with Meta IDs in seed/competitors.json
            Assert.Equal(5, metaIdentities.Count);
            Assert.All(metaIdentities, i => Assert.Equal(AdvertiserIdentityStatus.Pending, i.Status));

            // Simulate admin approving one identity
            var first = metaIdentities.First();
            first.Status = AdvertiserIdentityStatus.Approved;
            first.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Second Seed: should NOT duplicate identities, and must NOT reset approved status
        await DbInitializer.SeedCompetitorsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var metaIdentities = await db.AdvertiserIdentities
                .Where(i => i.Platform == AdPlatform.Meta)
                .ToListAsync();

            // Still exactly 5 identities (no duplicates)
            Assert.Equal(5, metaIdentities.Count);

            var approved = metaIdentities.Single(i => i.Status == AdvertiserIdentityStatus.Approved);
            Assert.NotNull(approved.ReviewedAt);

            var pending = metaIdentities.Where(i => i.Status == AdvertiserIdentityStatus.Pending).ToList();
            Assert.Equal(4, pending.Count);
        }
    }
}
