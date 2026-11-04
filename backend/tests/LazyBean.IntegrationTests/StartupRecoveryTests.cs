using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.IntegrationTests;

[Collection("Database")]
public class StartupRecoveryTests(TestDatabaseFixture testDb)
{
    [Fact]
    public async Task RecoverInterruptedRunsAsync_MarksRunningRunsAsFailedWithInterrupted()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(o => o.UseNpgsql(testDb.ConnectionString));
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        var sp = services.BuildServiceProvider();

        await using (var db = new LazyBeanDbContext(
            new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options))
        {
            var competitor = new Competitor
            {
                Key = $"recovery-test-{Guid.NewGuid():N}",
                Name = "Recovery Test Competitor",
                BaseUrl = "https://example.com",
                Platform = CompetitorPlatform.Shopify,
                Enabled = true
            };
            db.Competitors.Add(competitor);

            var stuckRun = new CollectionRun
            {
                Trigger = RunTrigger.Manual,
                Status = CollectionRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            db.CollectionRuns.Add(stuckRun);
            await db.SaveChangesAsync();

            var stuckCompetitorRun = new CompetitorRun
            {
                RunId = stuckRun.Id,
                CompetitorId = competitor.Id,
                Status = CompetitorRunStatus.Running,
                Coverage = RunCoverage.Partial,
                Pages = 2,
                Products = 50,
                Errors = 0
            };
            db.CompetitorRuns.Add(stuckCompetitorRun);
            await db.SaveChangesAsync();

            // Run recovery
            await DbInitializer.RecoverInterruptedRunsAsync(sp);

            // Assertions
            var recoveredRun = await db.CollectionRuns.AsNoTracking().SingleAsync(r => r.Id == stuckRun.Id);
            Assert.Equal(CollectionRunStatus.Failed, recoveredRun.Status);
            Assert.NotNull(recoveredRun.FinishedAt);

            var recoveredCr = await db.CompetitorRuns.AsNoTracking().SingleAsync(cr => cr.Id == stuckCompetitorRun.Id);
            Assert.Equal(CompetitorRunStatus.Failed, recoveredCr.Status);
            Assert.Equal(RunCoverage.Partial, recoveredCr.Coverage);
            Assert.Equal(1, recoveredCr.Errors);
        }
    }
}
