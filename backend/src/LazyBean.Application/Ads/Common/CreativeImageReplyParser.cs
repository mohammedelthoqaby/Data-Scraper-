using System.Globalization;
using System.Text.Json;
using LazyBean.Application.Common.Interfaces;

namespace LazyBean.Application.Ads.Common;

// Reads the model's answer for an image preview. The prompt asks for {"productName": ..., "priceEgp": ...}. The reply
// comes wrapped in Gemini's envelope, and the model may wrap its JSON in a code fence, so both are accepted.
public static class CreativeImageReplyParser
{
    private const int MaxNameLength = 300;
    private const decimal MaxPlausiblePrice = 1_000_000m;

    // A reply that cannot be read gives an empty reading, so the same image is not sent again and again.
    public static CreativeImageReading Parse(string geminiResponseBody)
    {
        try
        {
            using var envelope = JsonDocument.Parse(geminiResponseBody);
            var text = ModelText(envelope.RootElement);
            return text is null ? Empty : ParseModelJson(text);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    internal static CreativeImageReading ParseModelJson(string modelText)
    {
        var json = StripCodeFence(modelText);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var name = doc.RootElement.TryGetProperty("productName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? Clean(nameProp.GetString())
                : null;

            decimal? price = null;
            if (doc.RootElement.TryGetProperty("priceEgp", out var priceProp))
            {
                price = priceProp.ValueKind switch
                {
                    JsonValueKind.Number when priceProp.TryGetDecimal(out var number) => number,
                    JsonValueKind.String when decimal.TryParse(priceProp.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text) => text,
                    _ => null,
                };
            }

            if (price is null || price <= 0 || price > MaxPlausiblePrice)
            {
                price = null;
            }

            return new CreativeImageReading(name, price is null ? null : Math.Round(price.Value, 2));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static readonly CreativeImageReading Empty = new(null, null);

    // candidates[0].content.parts[0].text, or null when the envelope has no text (a blocked or empty reply).
    private static string? ModelText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var withoutOpening = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed[3..];
        return withoutOpening.EndsWith("```", StringComparison.Ordinal) ? withoutOpening[..^3].Trim() : withoutOpening.Trim();
    }

    private static string? Clean(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength];
    }
}
