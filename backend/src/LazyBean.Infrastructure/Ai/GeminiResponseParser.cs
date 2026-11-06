using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Ai.Dtos;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ai;

public static class GeminiResponseParser
{
    public static IReadOnlyList<AiReviewTriageResult> ParseTriageResults(
        string responseJson,
        IReadOnlyList<AiReviewTriagePairRequest> batch,
        ILogger? logger = null)
    {
        if (batch.Count == 0) return [];

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            LogParseFailure(logger, "Response is null or empty", "");
            return CreateFallbackUnsure(batch, "Empty response from Gemini");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            string? rawText = null;

            // 1. Standard Gemini API format: candidates[0].content.parts[0].text
            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
            {
                var cand = candidates[0];
                if (cand.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textProp))
                {
                    rawText = textProp.GetString();
                }
            }

            // If candidates not present, check if responseJson is directly the payload
            if (string.IsNullOrWhiteSpace(rawText))
            {
                rawText = responseJson;
            }

            var cleaned = StripMarkdownCodeFences(rawText.Trim());

            using var resultDoc = JsonDocument.Parse(cleaned);
            var resultRoot = resultDoc.RootElement;

            List<JsonElement> items = [];

            if (resultRoot.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in resultRoot.EnumerateArray())
                {
                    items.Add(el);
                }
            }
            else if (resultRoot.ValueKind == JsonValueKind.Object)
            {
                // Check common wrapper properties
                var foundArray = false;
                string[] arrayPropNames = ["reviews", "items", "results", "evaluations", "pairs", "candidates", "data"];
                foreach (var prop in resultRoot.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array &&
                        arrayPropNames.Any(name => string.Equals(name, prop.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var el in prop.Value.EnumerateArray())
                        {
                            items.Add(el);
                        }
                        foundArray = true;
                        break;
                    }
                }

                // If no array property found, check any array property
                if (!foundArray)
                {
                    foreach (var prop in resultRoot.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in prop.Value.EnumerateArray())
                            {
                                items.Add(el);
                            }
                            foundArray = true;
                            break;
                        }
                    }
                }

                // If still no array, check if it's a single triage item object
                if (!foundArray && (HasProperty(resultRoot, "verdict") || HasProperty(resultRoot, "reviewId") || HasProperty(resultRoot, "id")))
                {
                    items.Add(resultRoot);
                }
            }

            if (items.Count == 0)
            {
                LogParseFailure(logger, "No review items or arrays found in candidate text", cleaned);
                return CreateFallbackUnsure(batch, "AI response contained 0 items");
            }

            var results = new List<AiReviewTriageResult>();
            for (var i = 0; i < items.Count; i++)
            {
                var el = items[i];
                var parsed = ParseElement(el, i, batch);
                if (parsed != null)
                {
                    results.Add(parsed);
                }
            }

            if (results.Count == 0)
            {
                LogParseFailure(logger, "All candidate elements failed schema extraction", cleaned);
                return CreateFallbackUnsure(batch, "Could not extract valid review decisions");
            }

            return results;
        }
        catch (Exception ex)
        {
            LogParseFailure(logger, $"Parsing exception: {ex.Message}", responseJson);
            return CreateFallbackUnsure(batch, "JSON parse failure");
        }
    }

    private static AiReviewTriageResult? ParseElement(JsonElement el, int index, IReadOnlyList<AiReviewTriagePairRequest> batch)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        // 1. Resolve ReviewId
        Guid? reviewId = null;
        if (TryGetStringProperty(el, ["reviewId", "review_id", "id", "reviewid", "item_id"], out var idStr) &&
            Guid.TryParse(idStr, out var parsedGuid))
        {
            reviewId = parsedGuid;
        }

        // Positional fallback if reviewId was omitted, malformed, or index-based (e.g., "1", "item-0")
        AiReviewTriagePairRequest? matchingReq = null;
        if (reviewId.HasValue)
        {
            matchingReq = batch.FirstOrDefault(b => b.ReviewId == reviewId.Value);
        }

        if (matchingReq == null && index < batch.Count)
        {
            matchingReq = batch[index];
            reviewId = matchingReq.ReviewId;
        }

        if (!reviewId.HasValue || matchingReq == null)
        {
            return null;
        }

        // 2. Resolve Verdict
        var verdict = "unsure";
        if (TryGetStringProperty(el, ["verdict", "decision", "match", "status", "result"], out var vRaw))
        {
            var vLower = vRaw.Trim().ToLowerInvariant();
            if (vLower is "same" or "exact" or "match" or "identical")
            {
                verdict = "same";
            }
            else if (vLower is "different" or "diff" or "mismatch" or "no_match")
            {
                verdict = "different";
            }
            else
            {
                verdict = "unsure";
            }
        }

        // 3. Resolve Confidence
        var confidence = 0.5m;
        if (TryGetDecimalProperty(el, ["confidence", "score", "prob", "probability"], out var cVal))
        {
            confidence = Math.Clamp(cVal, 0.0m, 1.0m);
        }

        // 4. Resolve Reason
        var reason = "";
        if (TryGetStringProperty(el, ["reason", "explanation", "notes", "note", "justification"], out var rRaw))
        {
            reason = rRaw.Trim();
        }

        return new AiReviewTriageResult(reviewId.Value, matchingReq.PairHash, verdict, confidence, reason);
    }

    private static string StripMarkdownCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Matches ```json ... ``` or ``` ... ```
        var match = Regex.Match(text, @"^```(?:json)?\s*([\s\S]*?)\s*```$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // If it starts with ```json
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0)
            {
                text = text[(firstNl + 1)..];
            }
        }
        else if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0)
            {
                text = text[(firstNl + 1)..];
            }
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        return text.Trim();
    }

    private static bool HasProperty(JsonElement el, string name)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryGetStringProperty(JsonElement el, string[] candidates, out string value)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (candidates.Any(c => string.Equals(c, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    value = p.Value.GetString() ?? "";
                    return true;
                }
                value = p.Value.ToString();
                return true;
            }
        }
        value = "";
        return false;
    }

    private static bool TryGetDecimalProperty(JsonElement el, string[] candidates, out decimal value)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (candidates.Any(c => string.Equals(c, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var dec))
                {
                    value = dec;
                    return true;
                }
                if (p.Value.ValueKind == JsonValueKind.String && decimal.TryParse(p.Value.GetString(), out var sDec))
                {
                    value = sDec;
                    return true;
                }
            }
        }
        value = 0m;
        return false;
    }

    private static IReadOnlyList<AiReviewTriageResult> CreateFallbackUnsure(
        IReadOnlyList<AiReviewTriagePairRequest> batch,
        string reason)
    {
        return batch.Select(b => new AiReviewTriageResult(
            b.ReviewId,
            b.PairHash,
            "unsure",
            0.5m,
            reason)).ToList();
    }

    private static void LogParseFailure(ILogger? logger, string errorReason, string rawContent)
    {
        if (logger == null) return;
        var sanitized = Regex.Replace(rawContent ?? "", @"key=[A-Za-z0-9_\-\.]+", "key=***");
        if (sanitized.Length > 400)
        {
            sanitized = sanitized[..400] + "... [truncated]";
        }
        logger.LogWarning("Gemini triage response shape parse issue ({Reason}). Raw text snippet: {Snippet}", errorReason, sanitized);
    }
}
