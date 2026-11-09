using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ai;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai;

// Sends one Google creative preview image to Gemini and reads the product name and price from it (the optional AI-read).
// Only the Google preview hosts are fetched, so the reader cannot be pointed at an arbitrary address.
public sealed class GeminiCreativeImageReader(
    IHttpClientFactory httpClientFactory,
    LazyBeanDbContext dbContext,
    IAiKeyProtector protector,
    IOptions<AiOptions> options,
    ILogger<GeminiCreativeImageReader> logger) : ICreativeImageReader
{
    private const string BaseApiUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const int MaxImageBytes = 3 * 1024 * 1024;

    private const string Prompt =
        "This image is an advertisement from a coffee and beverage store in Egypt. " +
        "Reply with JSON only: {\"productName\": the product name written in the image, or null, " +
        "\"priceEgp\": the price in Egyptian pounds written in the image as a number, or null}. Do not guess.";

    private static readonly string[] AllowedHostSuffixes = ["googlesyndication.com", "googleusercontent.com"];
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp"];

    private readonly HttpClient _http = httpClientFactory.CreateClient("GeminiClient");

    public async Task<CreativeImageReading?> ReadAsync(string imageUrl, CancellationToken ct = default)
    {
        _ = await dbContext.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("AI is not configured.");

        // The Gemini key and model come from the provider settings. An environment variable overrides the saved key.
        var row = await dbContext.AiProviderSettings.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Provider == AiProviders.Gemini, ct);
        var apiKey = AiProviderConfigResolver.ResolveKey(AiProviders.Gemini, options.Value, row, protector, logger);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("No Gemini API key is saved.");
        }

        var model = AiProviderConfigResolver.ModelFor(AiProviders.Gemini, row);

        var image = await DownloadImageAsync(imageUrl, ct);
        if (image is null)
        {
            return null;
        }

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = Prompt },
                        new { inline_data = new { mime_type = image.Value.MimeType, data = Convert.ToBase64String(image.Value.Bytes) } },
                    },
                },
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0 },
        };

        var url = $"{BaseApiUrl}/{Uri.EscapeDataString(model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        // The key goes in a header, never in the URL, so it cannot end up in logs or proxies.
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // The status is reported, not the body: an error body can echo the request.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini returned HTTP {(int)response.StatusCode} for an image read.");
        }

        return CreativeImageReplyParser.Parse(body);
    }

    // The preview image, or null when it is not a Google preview, not an image we send, or too large.
    private async Task<(byte[] Bytes, string MimeType)?> DownloadImageAsync(string imageUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !AllowedHostSuffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The creative preview returned HTTP {(int)response.StatusCode}.");
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (mimeType is null || !AllowedImageTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (response.Content.Headers.ContentLength is > MaxImageBytes)
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
            if (buffer.Length > MaxImageBytes)
            {
                return null;
            }
        }

        return (buffer.ToArray(), mimeType.ToLowerInvariant());
    }
}
