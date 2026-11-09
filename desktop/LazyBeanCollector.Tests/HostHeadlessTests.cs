using System.Diagnostics;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Infrastructure.External.LazyBeanApi;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Collector.App.Host;
using LazyBean.Application.Collector.Orchestration;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Infrastructure.External.Images;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Security;
using LazyBean.Collector.App.Settings;
using LazyBean.Infrastructure.External.Meta;
using LazyBean.Application.Ads.Models;
using LazyBean.Application.Ads.Collection;

namespace LazyBean.Collector.App.Tests;

// The collector with no window at all: the host runs its own loop, so claims and heartbeats continue whether
// or not a form exists. Nothing in these tests creates a form.
public sealed class HostHeadlessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lazybean-host-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ClaimsAndHeartbeats_KeepGoing_WithNoWindow()
    {
        var paths = Paired();
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "Page A")], []),
        };
        var meta = new FakeMetaCollector();
        meta.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        api.Claims.Enqueue(_ => Task.FromResult<CollectorJobDto?>(Builders.AdsJob("MetaAds")));
        api.Claims.Enqueue(_ => Task.FromResult<CollectorJobDto?>(Builders.AdsJob("MetaAds")));

        await using var host = CreateHost(paths, api, new SlowMetaCollector(meta, TimeSpan.FromMilliseconds(300)));
        await host.StartAsync();

        // The second job is only handed out after the first finished, so this also shows the idle polls kept claiming.
        await WaitUntilAsync(() => api.JobCompletions.Count == 2, TimeSpan.FromSeconds(20));

        Assert.True(api.Heartbeats >= 2, $"Expected heartbeats while a job ran, got {api.Heartbeats}.");
    }

    [Fact]
    public async Task Exiting_MidJob_HandsTheClaimBack_AndCompletesNothing()
    {
        var paths = Paired();
        var job = Builders.AdsJob("MetaAds");
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "Page A")], []),
        };
        var meta = new FakeMetaCollector();
        meta.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        api.Claims.Enqueue(_ => Task.FromResult<CollectorJobDto?>(job));

        await using var host = CreateHost(paths, api, new SlowMetaCollector(meta, TimeSpan.FromSeconds(30)));
        await host.StartAsync();
        await WaitUntilAsync(() => api.Heartbeats >= 1, TimeSpan.FromSeconds(20));

        var stopwatch = Stopwatch.StartNew();
        await host.StopAsync();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Exit took {stopwatch.Elapsed}.");
        Assert.Equal([job.Id], api.Releases);
        Assert.Empty(api.JobCompletions);
    }

    private AppPaths Paired()
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        new SettingsStore(paths.SettingsFile).Save(new CollectorSettings
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Test PC",
            ServerUrl = "https://lazybean.test",
            PairedBy = "admin",
        });
        new TokenStore(paths.TokenFile).Save("device-token");
        return paths;
    }

    // Short timings: a heartbeat every 40 ms, and no waiting between idle polls or Meta pages.
    private static CollectorHost CreateHost(AppPaths paths, FakeCollectorApi api, IMetaPageCollector meta) =>
        new(
            paths,
            new ActivityLog(paths.LogsDirectory),
            new CollectorState(),
            new CollectorHostOptions(
                ApiFactory: _ => api,
                MetaCollector: meta,
                GoogleClient: new FakeGoogleClient(),
                Heartbeat: TimeSpan.FromMilliseconds(40),
                Delay: (duration, ct) => Task.Delay(TimeSpan.FromMilliseconds(Math.Min(20, duration.TotalMilliseconds)), ct)));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    // A Meta page that takes a while, so the job is still running while the test watches the heartbeats.
    private sealed class SlowMetaCollector(FakeMetaCollector inner, TimeSpan delay) : IMetaPageCollector
    {
        public async Task<MetaPageResult> CollectPageAsync(string pageId, string country, string status, int maxResults, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return await inner.CollectPageAsync(pageId, country, status, maxResults, ct);
        }
    }
}
