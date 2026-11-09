using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LazyBean.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Ai.Providers;

/// <summary>Groq via its OpenAI-compatible chat completions endpoint, in JSON object mode.</summary>
public sealed class GroqClient(HttpClient http, IOptions<AiOptions> options, ILogger<GroqClient> logger) : IAiProvider
{
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public string Name => AiProviders.Groq;

    public async Task<AiProviderResponse> SendAsync(AiProviderRequest request, string apiKey, string model, CancellationToken ct)
    {
        var baseUrl = (options.Value.Groq.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');

        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            temperature = 0,
            max_completion_tokens = request.MaxOutputTokens,
            response_format = new { type = "json_object" },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await AiHttp.SendAsync(http, message, RequestTimeout, Name, apiKey, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var rateLimit = AiHttp.ReadRateLimit(response);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Groq returned HTTP {Status}.", (int)response.StatusCode);
            throw AiHttp.ToException(response.StatusCode, raw, Name, apiKey, AiHttp.ReadRetryAfter(response), rateLimit);
        }

        var (text, totalTokens) = ParseCompletion(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException(AiProviderErrorKind.ServerError, "Groq returned an empty completion.");
        }

        return new AiProviderResponse(text, totalTokens, rateLimit);
    }

    internal static (string? Text, int TotalTokens) ParseCompletion(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string? text = null;
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
        }

        var total = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("total_tokens", out var totalProp) && totalProp.TryGetInt32(out var t))
                total = t;
        }

        return (text, total);
    }
}
