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

            // We have 5 competitors with Meta IDs in seed/competitors.json
            Assert.Equal(5, metaIdentities.Count);
            Assert.All(metaIdentities, i => Assert.Equal(AdvertiserIdentityStatus.Approved, i.Status));

            // Simulate admin rejecting one identity
            var rejected = metaIdentities.First();
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

            // Still exactly 5 identities (no duplicates)
            Assert.Equal(5, metaIdentities.Count);

            var rejected = metaIdentities.Single(i => i.Id == rejectedId);
            Assert.Equal(AdvertiserIdentityStatus.Rejected, rejected.Status);
            Assert.NotNull(rejected.ReviewedAt);

            Assert.Equal(4, metaIdentities.Count(i => i.Status == AdvertiserIdentityStatus.Approved));
        }
    }
}
