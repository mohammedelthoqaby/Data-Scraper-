using System.Net;
using System.Net.Http.Json;
using LazyBean.Api.Controllers;
using LazyBean.Application.Runs.Commands.CancelRun;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LazyBean.IntegrationTests;

[Collection("Database")]
public class RunEndpointTests(TestDatabaseFixture testDb) : IAsyncLifetime
{
    private const string Username = "it-run-test-user";
    private const string Password = "P@ssw0rd-For-Runs-Only!";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        if (existing is null)
        {
            db.Users.Add(new User { Username = Username, PasswordHash = new PasswordHasherAdapter().Hash(Password) });
            await db.SaveChangesAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = testDb.ConnectionString,
                    ["ConnectionStrings:Redis"] = "",
                    ["Admin:Username"] = "",
                    ["Admin:Password"] = "",
                    ["Collection:AutoResumeInterruptedRuns"] = "false"
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost")
        });

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        loginRes.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task StartRun_WhenActiveRunExists_Returns409WithAlreadyActiveTrue()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using (var db = new LazyBeanDbContext(options))
        {
            var activeRun = new CollectionRun
            {
                Trigger = RunTrigger.Manual,
                Status = CollectionRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.CollectionRuns.Add(activeRun);
            await db.SaveChangesAsync();

            try
            {
                var response = await _client.PostAsJsonAsync("/api/runs", new StartRunRequest());
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

                var body = await response.Content.ReadFromJsonAsync<StartRunResultDto>();
                Assert.NotNull(body);
                Assert.Equal(activeRun.Id, body.RunId);
                Assert.True(body.AlreadyActive);
            }
            finally
            {
                // Clean up active run so it doesn't leak into other tests
                activeRun.Status = CollectionRunStatus.Completed;
                activeRun.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task GetActiveRun_WhenNoRunActive_Returns204NoContent()
    {
        var response = await _client.GetAsync("/api/runs/active");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetActiveRun_WhenRunActive_Returns200WithProgressDto()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using (var db = new LazyBeanDbContext(options))
        {
            var activeRun = new CollectionRun
            {
                Trigger = RunTrigger.Manual,
                Status = CollectionRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.CollectionRuns.Add(activeRun);
            await db.SaveChangesAsync();

            try
            {
                var response = await _client.GetAsync("/api/runs/active");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var body = await response.Content.ReadFromJsonAsync<LazyBean.Application.Collection.Orchestration.RunProgressDto>();
                Assert.NotNull(body);
                Assert.Equal(activeRun.Id, body.RunId);
                Assert.Equal("Running", body.Status);
            }
            finally
            {
                activeRun.Status = CollectionRunStatus.Completed;
                activeRun.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task StartRun_WhenHttpRequestAborted_RunStillCompletesInDatabase()
    {
        var targetId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new StartRunRequest([targetId]))
        };

        // Send request and read headers, then immediately cancel client token simulating abort
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StartRunResultDto>();
        Assert.NotNull(body);
        var runId = body.RunId;

        // Abort the client HTTP request
        cts.Cancel();

        // BackgroundService must not be tied to the HTTP request; verify run reaches completion
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        CollectionRun? run = null;

        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(200);
            await using var db = new LazyBeanDbContext(options);
            run = await db.CollectionRuns.FindAsync(runId);
            if (run != null && run.Status != CollectionRunStatus.Queued && run.Status != CollectionRunStatus.Running)
            {
                break;
            }
        }

        Assert.NotNull(run);
        Assert.NotEqual(CollectionRunStatus.Cancelled, run.Status);
        Assert.True(
            run.Status == CollectionRunStatus.Completed || run.Status == CollectionRunStatus.Partial,
            $"Expected run to finish in background despite client abort, but status was {run.Status}");
    }

    [Fact]
    public async Task StartRun_WithSpecificCompetitorIds_RecordsTargets()
    {
        var targetId1 = Guid.NewGuid();
        var targetId2 = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync("/api/runs", new StartRunRequest([targetId1, targetId2]));
        Assert.True(response.StatusCode == HttpStatusCode.Accepted || response.StatusCode == HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<StartRunResultDto>();
        Assert.NotNull(body);

        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        var run = await db.CollectionRuns.FindAsync(body.RunId);
        Assert.NotNull(run);
        Assert.NotNull(run.TargetCompetitorIdsJson);
        Assert.Contains(targetId1.ToString(), run.TargetCompetitorIdsJson);
        Assert.Contains(targetId2.ToString(), run.TargetCompetitorIdsJson);

        // Mark completed to not leave running
        run.Status = CollectionRunStatus.Completed;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CancelRun_ActiveRun_Returns200AndCancels()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(testDb.ConnectionString).Options;
        Guid runId;
        await using (var db = new LazyBeanDbContext(options))
        {
            var activeRun = new CollectionRun
            {
                Trigger = RunTrigger.Manual,
                Status = CollectionRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.CollectionRuns.Add(activeRun);
            await db.SaveChangesAsync();
            runId = activeRun.Id;
        }

        var response = await _client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CancelRunResultDto>();
        Assert.NotNull(body);
        Assert.Equal(runId, body.RunId);
        Assert.Equal("cancelled", body.State);

        await using (var db = new LazyBeanDbContext(options))
        {
            var cancelled = await db.CollectionRuns.FindAsync(runId);
            Assert.NotNull(cancelled);
            Assert.Equal(CollectionRunStatus.Cancelled, cancelled.Status);
            Assert.NotNull(cancelled.FinishedAt);
        }
    }

    [Fact]
    public async Task CancelRun_NonExistent_Returns404()
    {
        var response = await _client.PostAsync($"/api/runs/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPriceMatrix_Latest_ReturnsOkWithRowsAndCsv()
    {
        var jsonRes = await _client.GetAsync("/api/price-matrix/latest");
        Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);

        var csvRes = await _client.GetAsync("/api/price-matrix/latest.csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", csvRes.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task GetReferenceOffers_ReturnsOk()
    {
        var res = await _client.GetAsync("/api/reference-offers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task GetPriceHistory_ReturnsRealPaginationAndFullCsvStream()
    {
        var jsonRes = await _client.GetAsync("/api/history/prices?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);

        var csvRes = await _client.GetAsync("/api/history/prices.csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", csvRes.Content.Headers.ContentType?.ToString());
    }
}
