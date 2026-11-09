using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LazyBean.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai.Providers;

/// <summary>Google Gemini via generateContent (fallback provider). The key is sent in a header, never in the URL.</summary>
public sealed class GeminiClient(HttpClient http, IOptions<AiOptions> options, ILogger<GeminiClient> logger) : IAiProvider
{
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public string Name => AiProviders.Gemini;

    public async Task<AiProviderResponse> SendAsync(AiProviderRequest request, string apiKey, string model, CancellationToken ct)
    {
        var baseUrl = (options.Value.Gemini.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        var url = $"{baseUrl}/models/{Uri.EscapeDataString(model)}:generateContent";

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = request.UserPrompt } } },
            },
            generationConfig = new
            {
                temperature = 0,
                maxOutputTokens = request.MaxOutputTokens,
                responseMimeType = "application/json",
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        using var response = await AiHttp.SendAsync(http, message, RequestTimeout, Name, apiKey, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini returned HTTP {Status}.", (int)response.StatusCode);

            // Gemini reports an invalid key as HTTP 400 with API_KEY_INVALID, not 401.
            if (response.StatusCode == HttpStatusCode.BadRequest
                && (raw.Contains("API_KEY_INVALID", StringComparison.Ordinal) || raw.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)))
            {
                throw new AiProviderException(AiProviderErrorKind.InvalidKey, "Google Gemini rejected the API key (API_KEY_INVALID).", 400);
            }

            throw AiHttp.ToException(response.StatusCode, raw, Name, apiKey, AiHttp.ReadRetryAfter(response), rateLimit: null);
        }

        var (text, totalTokens) = ParseGenerateContent(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException(AiProviderErrorKind.ServerError, "Google Gemini returned an empty completion.");
        }

        return new AiProviderResponse(text, totalTokens, RateLimit: null);
    }

    internal static (string? Text, int TotalTokens) ParseGenerateContent(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string? text = null;
        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var textProp)
            && textProp.ValueKind == JsonValueKind.String)
        {
            text = textProp.GetString();
        }

        var total = 0;
        if (root.TryGetProperty("usageMetadata", out var usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("totalTokenCount", out var totalProp)
            && totalProp.TryGetInt32(out var t))
        {
            total = t;
        }

        return (text, total);
    }
}
