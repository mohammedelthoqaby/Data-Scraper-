using System.Text.Json;
using LazyBean.Application.Matching;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Matching;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class OrphanedPhaseRecoveryTests
{
    private ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton<IMatchingQueue, ChannelMatchingQueue>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RecoverInterruptedRunsAsync_MarksRunningPhaseAsInterrupted()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);

        var runId = Guid.NewGuid();
        var initialPhases = new[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = "classification",
                ["status"] = "completed",
                ["message"] = "done"
            },
            new Dictionary<string, object?>
            {
                ["name"] = "matching",
                ["status"] = "Running",
                ["completedCategories"] = new[] { "CoffeeBeans" },
                ["message"] = "Category 1/11: CoffeeBeans (10 matches so far)"
            }
        };

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            db.CollectionRuns.Add(new CollectionRun
            {
                Id = runId,
                Status = CollectionRunStatus.Running,
                PhasesJson = JsonSerializer.Serialize(initialPhases)
            });
            await db.SaveChangesAsync();
        }

        // Run recovery
        await DbInitializer.RecoverInterruptedRunsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId);

            Assert.NotNull(run.PhasesJson);
            using var doc = JsonDocument.Parse(run.PhasesJson);
            var phases = doc.RootElement.EnumerateArray().ToList();

            var matchingPhase = phases.First(p => p.GetProperty("name").GetString() == "matching");
            Assert.Equal("interrupted", matchingPhase.GetProperty("status").GetString());
            Assert.Equal("Interrupted by server restart", matchingPhase.GetProperty("message").GetString());

            // Check that completedCategories is preserved
            var cats = matchingPhase.GetProperty("completedCategories").EnumerateArray().Select(c => c.GetString()).ToList();
            Assert.Contains("CoffeeBeans", cats);
        }
    }

    [Fact]
    public async Task RecoverInterruptedRunsAsync_EnqueuesMatchingJobWhenMatchingWasInterrupted()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var queue = sp.GetRequiredService<IMatchingQueue>();

        var runId = Guid.NewGuid();
        var competitorId = Guid.NewGuid();

        var initialPhases = new[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = "matching",
                ["status"] = "Running",
                ["completedCategories"] = new[] { "CoffeeBeans", "GroundCoffee" }
            }
        };

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            db.CollectionRuns.Add(new CollectionRun
            {
                Id = runId,
                Status = CollectionRunStatus.Running,
                PhasesJson = JsonSerializer.Serialize(initialPhases)
            });

            // Competitor run is completed (no unfinished competitors)
            db.CompetitorRuns.Add(new CompetitorRun
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                CompetitorId = competitorId,
                Status = CompetitorRunStatus.Completed,
                Products = 100
            });

            await db.SaveChangesAsync();
        }

        await DbInitializer.RecoverInterruptedRunsAsync(sp);

        // Read from queue
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var jobs = new List<MatchingJob>();
        await foreach (var job in queue.ReadAllAsync(cts.Token))
        {
            jobs.Add(job);
            break;
        }

        Assert.Single(jobs);
        Assert.Equal(runId, jobs[0].RunId);
    }
}
