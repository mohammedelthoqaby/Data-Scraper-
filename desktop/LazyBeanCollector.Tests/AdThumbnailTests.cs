using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using LazyBean.Application.Ads.Common;
using LazyBean.Collector.App.Jobs;
using LazyBean.Collector.App.Runtime;
using Xunit;

namespace LazyBean.Collector.App.Tests;

// The ad images on the desktop: the thumbnail the server stores (480 px wide, under 60 KB, never upscaled), and the sync that
// downloads and uploads them in batches and stops when an image host refuses.
public sealed class AdThumbnailTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "lazybean-thumbnail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_logs))
        {
            Directory.Delete(_logs, recursive: true);
        }
    }

    [Fact]
    public void Make_ShrinksAPhotoTo480PxWideAndUnder60KB()
    {
        var source = Png(1600, 1200);

        var thumbnail = AdThumbnailProcessor.Make(source);

        Assert.NotNull(thumbnail);
        Assert.True(JpegDimensions.TryRead(thumbnail!, out var width, out var height));
        Assert.True(width <= AdThumbnailRules.MaxWidth, $"width {width}");
        Assert.True(height <= AdThumbnailRules.MaxHeight, $"height {height}");
        Assert.True(thumbnail!.Length <= AdThumbnailProcessor.TargetBytes, $"{thumbnail.Length} bytes");
        Assert.Null(AdThumbnailRules.Reject(thumbnail));
    }

    [Fact]
    public void Make_DoesNotUpscaleASmallImage()
    {
        var thumbnail = AdThumbnailProcessor.Make(Png(100, 80));

        Assert.NotNull(thumbnail);
        Assert.True(JpegDimensions.TryRead(thumbnail!, out var width, out var height));
        Assert.Equal(100, width);
        Assert.Equal(80, height);
    }

    [Fact]
    public void Make_ReturnsNullForWhatIsNotAnImage()
    {
        Assert.Null(AdThumbnailProcessor.Make("not an image at all"u8.ToArray()));
    }

    [Fact]
    public async Task ScriptPreviews_AreNeverRequested()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, Png(40, 40));
        var processor = new AdThumbnailProcessor(new HttpClient(handler));

        var thumbnail = await processor.ReadThumbnailAsync(
            "https://displayads-formats.googleusercontent.com/ads/preview/content.js?creativeId=1", CancellationToken.None);

        Assert.Null(thumbnail);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Sync_UploadsInBatchesOfTen_AndStopsWhenAnImageHostRefuses()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, Png(200, 150));
        var processor = new AdThumbnailProcessor(new HttpClient(handler));
        var api = new FakeCollectorApi();
        var delays = new DelayRecorder();
        var sync = new AdThumbnailSync(api, processor, Log(), delays.Delay);

        var candidates = Enumerable.Range(1, 11)
            .Select(i => new AdThumbnailSync.Candidate($"ad{i:D2}", [$"https://tpc.googlesyndication.com/archive/simgad/{i}"]))
            .Append(new AdThumbnailSync.Candidate("ad12", ["https://tpc.googlesyndication.com/archive/simgad/refuse"]))
            .ToList();
        handler.RefuseUrlsContaining = "refuse";

        await sync.SyncAsync("Google", "AR01064852753525768193", candidates, CancellationToken.None);

        Assert.Equal([10, 1], api.ThumbnailUploads.Select(r => r.Items.Count).ToArray());
        Assert.Equal(11, api.ThumbnailUploads.Sum(r => r.Items.Count));
        Assert.True(delays.Delays.Count >= 11, "a pause before each image");
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // A smooth gradient, the kind of picture an ad has. Noise would not compress into the target size.
                bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 255) / Math.Max(1, width), (y * 255) / Math.Max(1, height), 128));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private ActivityLog Log() => new(_logs);

    private sealed class CountingHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string? RefuseUrlsContaining { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (RefuseUrlsContaining is not null && url.Contains(RefuseUrlsContaining, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }
}
