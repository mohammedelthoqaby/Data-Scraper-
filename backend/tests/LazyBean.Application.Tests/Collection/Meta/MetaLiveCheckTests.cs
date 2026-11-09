using LazyBean.Infrastructure.External.Meta;
using LazyBean.Application.Ads.Models;
using LazyBean.Application.Ads.Collection;
using Xunit.Abstractions;

namespace LazyBean.Application.Tests.Collection.Meta;

// Manual check against the real Meta Ad Library. Skipped unless LAZYBEAN_LIVE_META=1 so CI and
// normal test runs never touch the network. Reports the same control/Strong Coffee comparison the
// Python collector produced from this machine.
public class MetaLiveCheckTests(ITestOutputHelper output)
{
    private const string ControlPageId = "109786085108938";
    private const string StrongCoffeePageId = "102631558265863";

    [Theory]
    [InlineData(ControlPageId)]
    [InlineData(StrongCoffeePageId)]
    public async Task LivePage_ReportsStatusAndAdCount(string pageId)
    {
        if (Environment.GetEnvironmentVariable("LAZYBEAN_LIVE_META") != "1")
        {
            return;
        }

        var result = await MetaTestCollector.For(MetaAdsOptions.Default, null).CollectPageAsync(pageId, "EG", "ALL", 50);

        output.WriteLine($"page={result.PageId} status={result.Status} ads={result.TotalCollected} errorType={result.ErrorType} error={result.Error}");
        foreach (var ad in result.Ads.Take(3))
        {
            output.WriteLine($"  ad id={ad.Id} start={ad.StartDate} status={ad.Status} title={ad.Title} snapshot={ad.SnapshotUrl}");
        }

        Assert.Contains(result.Status, new[] { "Success", "Blocked", "Failed" });
    }
}
