using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using LazyBean.Application.Ads.Common;

namespace LazyBean.Collector.App.Jobs;

// Downloads an ad's image and makes the thumbnail the server stores: a JPEG at most 480 px wide, targeting under 60 KB.
// Only plain image addresses are read. A script or HTML preview is never fetched, and nothing is upscaled.
public sealed class AdThumbnailProcessor(HttpClient http)
{
    public const int TargetBytes = 60 * 1024;

    private const long MaxDownloadBytes = 3L * 1024 * 1024;
    private static readonly int[] Widths = [AdThumbnailRules.MaxWidth, 360, 300];
    private static readonly long[] Qualities = [70, 60, 50, 40];

    // Null when the address is not a plain image, the download fails, or no encoding fits the server's limit.
    // A refusal from the image host (403 or 429) is thrown, so the caller can stop reading images for this run.
    public async Task<byte[]?> ReadThumbnailAsync(string imageUrl, CancellationToken ct)
    {
        if (AdThumbnailRules.SourceKey(imageUrl) is null)
        {
            return null;
        }

        var source = await DownloadAsync(imageUrl, ct);
        return source is null ? null : Make(source);
    }

    // The smallest encoding that reaches the target, or the smallest one tried when none does, if the server accepts it.
    public static byte[]? Make(byte[] source)
    {
        Bitmap original;
        try
        {
            using var stream = new MemoryStream(source);
            original = new Bitmap(stream);
        }
        catch (ArgumentException)
        {
            return null;
        }

        using (original)
        {
            byte[]? smallest = null;
            foreach (var width in Widths)
            {
                using var scaled = Scale(original, width);
                foreach (var quality in Qualities)
                {
                    var bytes = EncodeJpeg(scaled, quality);
                    if (bytes.Length <= TargetBytes)
                    {
                        return bytes;
                    }

                    if (smallest is null || bytes.Length < smallest.Length)
                    {
                        smallest = bytes;
                    }
                }
            }

            return smallest is not null && smallest.Length <= AdThumbnailRules.MaxImageBytes ? smallest : null;
        }
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException($"The image host refused the request ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxDownloadBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    // Scales the picture down to fit the width and the height limit, keeping its shape. A small picture is copied as it is.
    private static Bitmap Scale(Bitmap source, int maxWidth)
    {
        var ratio = Math.Min(1.0, Math.Min((double)maxWidth / source.Width, (double)AdThumbnailRules.MaxHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));

        var target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return target;
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

        using var stream = new MemoryStream();
        bitmap.Save(stream, codec, parameters);
        return stream.ToArray();
    }
}
