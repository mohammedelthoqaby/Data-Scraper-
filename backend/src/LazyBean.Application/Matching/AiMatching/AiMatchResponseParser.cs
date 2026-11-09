using System.Globalization;
using System.Text.Json;

namespace LazyBean.Application.Matching.AiMatching;

public sealed record AiMatchJudgement(string Verdict, decimal Confidence, string Reason);

/// <summary>
/// Strict parser for the matching reply. A reply is usable only if it is JSON with a results
/// array. Entries with an unknown pair number, verdict or confidence are dropped one by one.
/// A reply with no usable entries returns null and the whole batch is treated as malformed.
/// </summary>
public static class AiMatchResponseParser
{
    private const int MaxReasonLength = 300;

    public static IReadOnlyDictionary<int, AiMatchJudgement>? Parse(string? text, int pairCount)
    {
        if (string.IsNullOrWhiteSpace(text) || pairCount <= 0)
            return null;

        try
        {
            using var document = JsonDocument.Parse(StripCodeFences(text.Trim()));
            var root = document.RootElement;

            JsonElement results;
            if (root.ValueKind == JsonValueKind.Array)
            {
                results = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("results", out results)
                     && results.ValueKind == JsonValueKind.Array)
            {
                // results already set
            }
            else
            {
                return null;
            }

            var judgements = new Dictionary<int, AiMatchJudgement>();
            foreach (var entry in results.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryReadPair(entry, out var pair) || pair < 1 || pair > pairCount)
                    continue;

                if (!entry.TryGetProperty("verdict", out var verdictProp) || verdictProp.ValueKind != JsonValueKind.String)
                    continue;

                var verdict = verdictProp.GetString()!.Trim().ToLowerInvariant();
                if (verdict is not ("same" or "different" or "unsure"))
                    continue;

                if (!TryReadConfidence(entry, out var confidence))
                    continue;

                var reason = entry.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                    ? Clean(reasonProp.GetString())
                    : string.Empty;

                judgements.TryAdd(pair, new AiMatchJudgement(verdict, confidence, reason));
            }

            return judgements.Count == 0 ? null : judgements;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadPair(JsonElement entry, out int pair)
    {
        pair = 0;
        if (!entry.TryGetProperty("pair", out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number)
            return prop.TryGetInt32(out pair);

        return prop.ValueKind == JsonValueKind.String
               && int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pair);
    }

    private static bool TryReadConfidence(JsonElement entry, out decimal confidence)
    {
        confidence = 0m;
        if (!entry.TryGetProperty("confidence", out var prop))
            return false;

        var parsed = prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDecimal(out var number) ? number : (decimal?)null,
            JsonValueKind.String => decimal.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) ? text : null,
            _ => null,
        };

        if (parsed is null || parsed < 0m || parsed > 1m)
            return false;

        confidence = parsed.Value;
        return true;
    }

    private static string Clean(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return string.Empty;

        var printable = new string(reason.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return printable.Length <= MaxReasonLength ? printable : printable[..MaxReasonLength];
    }

    private static string StripCodeFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewline = text.IndexOf('\n');
        var body = firstNewline >= 0 ? text[(firstNewline + 1)..] : text;
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3];
        return body.Trim();
    }
}
