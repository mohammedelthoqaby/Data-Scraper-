using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;
using Xunit;

namespace LazyBean.Collector.App.Tests;

// Reading the details of the creatives the server flags, after the listing upload of each advertiser.
public sealed class GoogleCreativeDetailHandlerTests : IDisposable
{
    private const string Advertiser = "AR01064852753525768193";
    private const string OtherAdvertiser = "AR00000000000000000002";

    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-detail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public async Task DetailsAreRead_OnlyForTheCreativesTheServerFlags_AndSentBack()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [Builders.GoogleIdentity(Advertiser, "Strong Coffee")]),
            GoogleItemResults = _ =>
            [
                new LazyBean.Application.Ads.Dtos.AdsIngestItemResult("c1", "created", null, NeedsDetail: true),
                new LazyBean.Application.Ads.Dtos.AdsIngestItemResult("c2", "updated", null, NeedsDetail: false),
            ],
        };
        var client = new FakeGoogleClient();
        client.Pages[Advertiser] = Pages(Builders.Creative("c1"), Builders.Creative("c2"));
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["c1"], client.DetailsRequested);
        var sent = Assert.Single(api.GoogleDetailIngests);
        Assert.Equal(Advertiser, sent.AdvertiserId);
        Assert.Equal("c1", Assert.Single(sent.Items).ExternalAdId);
    }

    [Fact]
    public async Task ADetailThatFailsOnTheNetwork_IsSkipped_AndTheOthersAreRead()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [Builders.GoogleIdentity(Advertiser, "Strong Coffee")]),
            GoogleItemResults = _ =>
            [
                new LazyBean.Application.Ads.Dtos.AdsIngestItemResult("c1", "created", null, NeedsDetail: true),
                new LazyBean.Application.Ads.Dtos.AdsIngestItemResult("c2", "created", null, NeedsDetail: true),
            ],
        };
        var client = new FakeGoogleClient
        {
            DetailFailure = id => id == "c1" ? new HttpRequestException("connection reset") : null,
        };
        client.Pages[Advertiser] = Pages(Builders.Creative("c1"), Builders.Creative("c2"));
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["c1", "c2"], client.DetailsRequested);
        Assert.Equal("c2", Assert.Single(Assert.Single(api.GoogleDetailIngests).Items).ExternalAdId);
        Assert.Contains(outcome.Pages!, p => p.Status == "Success");
    }

    [Fact]
    public async Task AGoogleBlockOnADetail_StopsTheRun_AndDoesNotBackTheAdvertiserOff()
    {
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [
                Builders.GoogleIdentity(Advertiser, "First"),
                Builders.GoogleIdentity(OtherAdvertiser, "Second"),
            ]),
            GoogleItemResults = request => request.AdvertiserId == Advertiser
                ? [new LazyBean.Application.Ads.Dtos.AdsIngestItemResult("c1", "created", null, NeedsDetail: true)]
                : [],
        };
        var client = new FakeGoogleClient
        {
            DetailFailure = id => id == "c1" ? new GoogleAdsBlockedException("captcha") : null,
        };
        client.Pages[Advertiser] = Pages(Builders.Creative("c1"));
        client.Pages[OtherAdvertiser] = Pages(Builders.Creative("x1"));
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        var outcome = await handler.HandleAsync(CancellationToken.None);

        // The ads of the first advertiser were saved, so its page is a success. The block does not back it off, and the run
        // does not go on to the second advertiser.
        Assert.Contains(outcome.Pages!, p => p.Status == "Success");
        Assert.DoesNotContain(api.GoogleIngests, i => i.Status == "Blocked");
        Assert.Equal([Advertiser], client.Requested);
    }

    [Fact]
    public async Task DetailsAreSentInBatchesOfTwenty()
    {
        var creatives = Enumerable.Range(1, 25).Select(i => Builders.Creative($"c{i:D2}")).ToArray();
        var api = new FakeCollectorApi
        {
            Identities = new CollectorIdentities([], [Builders.GoogleIdentity(Advertiser, "Strong Coffee")]),
            GoogleItemResults = _ => creatives
                .Select(c => new LazyBean.Application.Ads.Dtos.AdsIngestItemResult(c.ExternalAdId, "created", null, NeedsDetail: true))
                .ToList(),
        };
        var client = new FakeGoogleClient();
        client.Pages[Advertiser] = Pages(creatives);
        var handler = new GoogleJobHandler(api, client, Log(), new CollectorState(), new DelayRecorder().Delay);

        await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(25, client.DetailsRequested.Count);
        Assert.Equal([20, 5], api.GoogleDetailIngests.Select(r => r.Items.Count).ToArray());
    }

    private static Queue<GoogleCreativesPageResult> Pages(params GoogleCreativeDto[] creatives) =>
        new([new GoogleCreativesPageResult(creatives, null)]);

    private ActivityLog Log() => new(_logs);
}
