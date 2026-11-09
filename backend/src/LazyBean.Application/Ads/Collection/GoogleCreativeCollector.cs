using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;

namespace LazyBean.Application.Ads.Collection;

// Reads the creatives of one Google advertiser over its result pages: at most four pages and 100 creatives. The caller
// decides how long to pause before each further page (the desktop and the server use their own pauses), and whether the
// first page is preceded by a pause. Shared by the desktop app and the server's Google sync, so both read the same way.
public static class GoogleCreativeCollector
{
    public const int MaxPagesPerAdvertiser = 4;
    public const int MaxCreativesPerAdvertiser = 100;

    public sealed record Result(IReadOnlyList<GoogleCreativeDto> Creatives, int Pages);

    public static async Task<Result> CollectAsync(
        IGoogleAdsTransparencyClient client,
        string advertiserId,
        Func<CancellationToken, Task> pauseBeforeNextPage,
        CancellationToken ct)
    {
        var creatives = new List<GoogleCreativeDto>();
        var page = await client.SearchCreativesAsync(advertiserId, null, ct);
        creatives.AddRange(page.Creatives);

        var pageCount = 1;
        while (!string.IsNullOrWhiteSpace(page.NextPageToken) &&
               creatives.Count < MaxCreativesPerAdvertiser &&
               pageCount < MaxPagesPerAdvertiser)
        {
            await pauseBeforeNextPage(ct);
            page = await client.SearchCreativesAsync(advertiserId, page.NextPageToken, ct);
            creatives.AddRange(page.Creatives);
            pageCount++;
        }

        return new Result(creatives, pageCount);
    }
}
