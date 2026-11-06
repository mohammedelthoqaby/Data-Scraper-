using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collectors.Meta;

namespace LazyBean.Collector.App.Tests;

public sealed class StoreJobHandlerTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-handler-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public async Task SendsProductsInBatchesOf50_ThenCompletesWithTotals()
    {
        var api = new FakeCollectorApi();
        var handler = new StoreJobHandler(api, new FakeAdapter(Enumerable.Range(1, 120).Select(Builders.Product)), Log(), new CollectorState());
        var runId = Guid.NewGuid();

        var outcome = await handler.HandleAsync(Builders.StoreJob(runId), CancellationToken.None);

        Assert.Equal(["products:1", "products:2", "products:3", "competitor:Completed"], api.Calls);
        Assert.Equal([50, 50, 20], api.ProductBatches.Select(b => b.Request.Products.Count));
        Assert.All(api.ProductBatches, b => Assert.Equal(runId, b.RunId));

        var completion = Assert.Single(api.CompetitorCompletions).Request;
        Assert.Equal("Completed", completion.Status);
        Assert.Equal(3, completion.TotalPages);
        Assert.Equal(120, completion.TotalProducts);
        Assert.Null(completion.Error);
        Assert.Contains("120", outcome.Summary);
    }

    [Fact]
    public async Task DegradedAdapter_ReportsFailed_SoProductsAreNotRemoved()
    {
        var api = new FakeCollectorApi();
        var adapter = new FakeAdapter(
            Enumerable.Range(1, 3).Select(Builders.Product),
            beforeFinish: () => CollectionRunContext.MarkDegraded("the store stopped answering"));
        var handler = new StoreJobHandler(api, adapter, Log(), new CollectorState());

        var outcome = await handler.HandleAsync(Builders.StoreJob(Guid.NewGuid()), CancellationToken.None);

        var completion = Assert.Single(api.CompetitorCompletions).Request;
        Assert.Equal("Failed", completion.Status);
        Assert.StartsWith("Degraded: the store stopped answering", completion.Error);
        Assert.Contains("not changed", outcome.Summary);
    }

    [Fact]
    public async Task NonWooCommerceCompetitor_IsRefused()
    {
        var api = new FakeCollectorApi();
        var handler = new StoreJobHandler(api, new FakeAdapter([]), Log(), new CollectorState());
        var job = Builders.StoreJob(Guid.NewGuid()) with
        {
            Competitor = Builders.StoreJob(Guid.NewGuid()).Competitor! with { Platform = "Shopify" },
        };

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => handler.HandleAsync(job, CancellationToken.None));

        Assert.Contains("Shopify", error.Message);
        Assert.Empty(api.Calls);
    }

    private ActivityLog Log() => new(_logs);
}

public sealed class MetaJobHandlerTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-handler-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public async Task CollectsEachPage_PausesBetweenRequests_AndSkipsBlockedPages()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities(
            [
                Builders.MetaIdentity("1001", "Page A"),
                Builders.MetaIdentity("1002", "Page B"),
                Builders.MetaIdentity("1003", "Page C", DateTimeOffset.UtcNow.AddHours(5)),
            ], []),
        };
        var collector = new FakeMetaCollector();
        collector.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"), Builders.Ad("A2"));
        collector.Results["1002"] = Builders.MetaResult("1002", "Blocked");
        var delays = new DelayRecorder();
        var handler = new MetaJobHandler(api, collector, Log(), new CollectorState(), delays.Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["1001", "1002"], collector.PagesRequested);
        Assert.Equal(["meta:1001:Success", "meta:1002:Blocked"], api.Calls);
        Assert.Single(delays.Delays);
        Assert.InRange(delays.Delays[0].TotalSeconds, 20, 40);
        Assert.Equal("Meta: 2 ad(s) from 1 of 3 page(s).", outcome.Summary);
        Assert.Contains(outcome.Pages!, p => p.PageId == "1003" && p.Status == "Blocked");
    }

    [Fact]
    public async Task MapsAdsToTheServerShape()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "Page A")], []),
        };
        var collector = new FakeMetaCollector();
        collector.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        var handler = new MetaJobHandler(api, collector, Log(), new CollectorState(), new DelayRecorder().Delay);

        await handler.HandleAsync(CancellationToken.None);

        var ad = Assert.Single(Assert.Single(api.MetaIngests).Ads!);
        Assert.Equal("A1", ad.Id);
        Assert.Equal("عرض خاص", ad.Body);
        Assert.Equal("Active", ad.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero), ad.StartDate);
        Assert.Null(ad.StopDate);
    }

    [Fact]
    public async Task ServerRejectsOnePage_TheRestStillRun()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([Builders.MetaIdentity("1001", "A"), Builders.MetaIdentity("1002", "B")], []),
            MetaIngestFailure = request => request.PageId == "1001"
                ? new CollectorApiException(ApiErrorKind.NotFound, "no identity")
                : null,
        };
        var collector = new FakeMetaCollector();
        collector.Results["1001"] = Builders.MetaResult("1001", "Success", Builders.Ad("A1"));
        collector.Results["1002"] = Builders.MetaResult("1002", "Success", Builders.Ad("B1"));
        var handler = new MetaJobHandler(api, collector, Log(), new CollectorState(), new DelayRecorder().Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["meta:1002:Success"], api.Calls.Where(c => c.StartsWith("meta:", StringComparison.Ordinal) && !c.Contains("1001")));
        Assert.Contains(outcome.Pages!, p => p.PageId == "1001" && p.Status == "Failed");
        Assert.Contains(outcome.Pages!, p => p.PageId == "1002" && p.Status == "Success");
    }

    private ActivityLog Log() => new(_logs);
}

public sealed class GoogleJobHandlerTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-handler-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public async Task IngestsCreatives_WithAPauseBeforeEachAdvertiser()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [Builders.GoogleIdentity("AR01064852753525768193", "Strong Coffee")]),
        };
        var client = new FakeGoogleClient();
        client.Pages["AR01064852753525768193"] = new Queue<LazyBean.Application.Ads.Dtos.GoogleCreativesPageResult>(
        [
            new([Builders.Creative("c1"), Builders.Creative("c2")], null),
        ]);
        var delays = new DelayRecorder();
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), delays.Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        var ingest = Assert.Single(api.GoogleIngests);
        Assert.Equal("Success", ingest.Status);
        Assert.Equal(2, ingest.Ads!.Count);
        Assert.Equal("c1", ingest.Ads[0].ExternalAdId);
        Assert.Single(delays.Delays);
        Assert.InRange(delays.Delays[0].TotalSeconds, 3, 5);
        Assert.Equal("Google: 2 ad(s) from 1 of 1 advertiser(s).", outcome.Summary);
    }

    [Fact]
    public async Task AdvertiserWithoutAnArIdentifier_IsNotQueried()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [Builders.GoogleIdentity("cafelax.com", "Cafelax")]),
        };
        var client = new FakeGoogleClient();
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        Assert.Empty(client.Requested);
        Assert.Empty(api.GoogleIngests);
        Assert.Contains(outcome.Pages!, p => p.Status == "Failed" && p.Error!.Contains("Discover"));
    }

    [Fact]
    public async Task Block_IsReportedToTheServer_AndStopsTheRun()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [
                Builders.GoogleIdentity("AR00000000000000000001", "First"),
                Builders.GoogleIdentity("AR00000000000000000002", "Second"),
            ]),
        };
        var client = new FakeGoogleClient
        {
            Failure = id => id == "AR00000000000000000001" ? new GoogleAdsBlockedException("captcha") : null,
        };
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        await handler.HandleAsync(CancellationToken.None);

        var blocked = Assert.Single(api.GoogleIngests);
        Assert.Equal("Blocked", blocked.Status);
        Assert.Equal("captcha", blocked.Error);
        Assert.Equal(["AR00000000000000000001"], client.Requested);
    }

    private ActivityLog Log() => new(_logs);
}
