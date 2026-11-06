using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Tests;

// The worker loop with the real handlers: order is the server's, every job is completed, and failures are
// reported without ending the loop. The loop stops itself once the scripted queue is empty.
public sealed class JobRunnerTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-runner-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public async Task RunsTheJobsTheServerHandsOut_InOrder_AndCompletesEach()
    {
        var api = new FakeCollectorApi();
        var runId = Guid.NewGuid();
        var store = Builders.StoreJob(runId);
        var meta = Builders.AdsJob("MetaAds");
        var google = Builders.AdsJob("GoogleAds");
        api.Identities = new CollectorIdentities(
            [Builders.MetaIdentity("1001", "Page A")],
            [Builders.GoogleIdentity("AR00000000000000000001", "Google A")]);
        var metaCollector = new FakeMetaCollector();
        metaCollector.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        var googleClient = new FakeGoogleClient();
        googleClient.Pages["AR00000000000000000001"] = new Queue<GoogleCreativesPageResult>([new([Builders.Creative("g1")], null)]);

        Enqueue(api, store, meta, google);
        using var cts = new CancellationTokenSource();
        var runner = Runner(api, cts, new CollectorState(), metaCollector, googleClient);

        await runner.RunAsync(cts.Token);

        Assert.Equal(["Completed", "Completed", "Completed"], api.JobCompletions.Select(j => j.Request.Status));
        Assert.Equal([store.Id, meta.Id, google.Id], api.JobCompletions.Select(j => j.JobId));
        Assert.Equal(["Strong Coffee", "Meta", "Google"], Sources(api));
        Assert.Equal(["products:1", "competitor:Completed"], api.Calls.Where(c => c.StartsWith("products") || c.StartsWith("competitor")));
    }

    [Fact]
    public async Task RecordsEachSourceAsLastSync()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "Page A")], []),
        };
        var metaCollector = new FakeMetaCollector();
        metaCollector.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        var state = new CollectorState();
        Enqueue(api, Builders.AdsJob("MetaAds"));
        using var cts = new CancellationTokenSource();

        await Runner(api, cts, state, metaCollector, new FakeGoogleClient()).RunAsync(cts.Token);

        Assert.Contains("meta", state.LastSync().Keys);
        Assert.Contains("1 ad", state.LastSync()["meta"].Result);
    }

    [Fact]
    public async Task RevokedToken_EndsTheLoop_WithPairingRequired()
    {
        var api = new FakeCollectorApi();
        api.Claims.Enqueue(_ => Task.FromException<CollectorJobDto?>(
            new CollectorApiException(ApiErrorKind.Unauthorized, "revoked", System.Net.HttpStatusCode.Unauthorized)));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<PairingRequiredException>(() =>
            Runner(api, cts, new CollectorState(), new FakeMetaCollector(), new FakeGoogleClient()).RunAsync(cts.Token));
    }

    [Fact]
    public async Task ServerUnreachable_WaitsTenSecondsAndPollsAgain()
    {
        var api = new FakeCollectorApi();
        var store = Builders.StoreJob(Guid.NewGuid());
        api.Claims.Enqueue(_ => Task.FromException<CollectorJobDto?>(
            new CollectorApiException(ApiErrorKind.Offline, "Could not reach the server.")));
        api.Claims.Enqueue(_ => Task.FromResult<CollectorJobDto?>(store));
        var state = new CollectorState();
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var runner = Runner(api, cts, state, new FakeMetaCollector(), new FakeGoogleClient(), delays);

        await runner.RunAsync(cts.Token);

        Assert.Equal(TimeSpan.FromSeconds(10), delays[0]);
        Assert.Single(api.JobCompletions);
    }

    [Fact]
    public async Task AFailingJob_IsReportedAsFailed_AndTheLoopKeepsGoing()
    {
        var api = new FakeCollectorApi();
        var badStore = Builders.StoreJob(Guid.NewGuid(), "strong-coffee") with
        {
            Competitor = Builders.StoreJob(Guid.NewGuid()).Competitor! with { Platform = "Shopify" },
        };
        var goodMeta = Builders.AdsJob("MetaAds");
        api.Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "Page A")], []);
        var metaCollector = new FakeMetaCollector();
        metaCollector.Results["1001"] = Builders.MetaResult("1001", "Success");
        Enqueue(api, badStore, goodMeta);
        using var cts = new CancellationTokenSource();

        await Runner(api, cts, new CollectorState(), metaCollector, new FakeGoogleClient()).RunAsync(cts.Token);

        Assert.Equal(["Failed", "Completed"], api.JobCompletions.Select(j => j.Request.Status));
        Assert.Contains("Shopify", api.JobCompletions[0].Request.Summary);
    }

    [Fact]
    public async Task ClaimLostToTheServer_IsLoggedAndNotFatal()
    {
        var api = new FakeCollectorApi
        {
            JobCompleteFailure = _ => new CollectorApiException(ApiErrorKind.Conflict, "not yours", System.Net.HttpStatusCode.Conflict),
        };
        api.Identities = new CollectorIdentities([], []);
        Enqueue(api, Builders.AdsJob("MetaAds"));
        using var cts = new CancellationTokenSource();

        await Runner(api, cts, new CollectorState(), new FakeMetaCollector(), new FakeGoogleClient()).RunAsync(cts.Token);

        Assert.Empty(api.JobCompletions);
    }

    private static void Enqueue(FakeCollectorApi api, params CollectorJobDto[] jobs)
    {
        foreach (var job in jobs)
        {
            var captured = job;
            api.Claims.Enqueue(_ => Task.FromResult<CollectorJobDto?>(captured));
        }
    }

    private static string[] Sources(FakeCollectorApi api) => api.JobCompletions.Select(j => j.Request.Summary!.Split(':')[0]).ToArray();

    private JobRunner Runner(
        FakeCollectorApi api,
        CancellationTokenSource cts,
        CollectorState state,
        FakeMetaCollector meta,
        FakeGoogleClient google,
        List<TimeSpan>? delays = null)
    {
        var log = new ActivityLog(_logs);
        var store = new StoreJobHandler(api, new FakeAdapter(Enumerable.Range(1, 3).Select(Builders.Product)), log, state);
        var metaHandler = new MetaJobHandler(api, meta, log, state, (_, _) => Task.CompletedTask);
        var googleHandler = new GoogleJobHandler(api, google, log, state, (_, _) => Task.CompletedTask);

        // Stop once the server has nothing left and the loop is idle (the 10 s poll).
        Func<TimeSpan, CancellationToken, Task> delay = (duration, _) =>
        {
            delays?.Add(duration);
            if (duration == JobRunner.IdlePoll && api.Claims.Count == 0)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        };

        return new JobRunner(api, store, metaHandler, googleHandler, log, state, delay, heartbeatInterval: Timeout.InfiniteTimeSpan);
    }
}
