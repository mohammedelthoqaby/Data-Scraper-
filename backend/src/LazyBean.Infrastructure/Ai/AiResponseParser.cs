using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Parses the model's text output (provider-agnostic) into triage verdicts and ranked
/// attention items. Tolerant of wrapper objects, code fences and index-based ids, and
/// falls back to safe defaults instead of throwing.
/// </summary>
public static class AiResponseParser
{
    private static readonly string[] WrapperArrayNames = ["results", "reviews", "items", "evaluations", "pairs", "candidates", "data", "ranking"];

    public static IReadOnlyList<AiReviewTriageResult> ParseTriageResults(
        string text,
        IReadOnlyList<AiReviewTriagePairRequest> batch,
        ILogger? logger = null)
    {
        if (batch.Count == 0) return [];

        if (string.IsNullOrWhiteSpace(text))
        {
            LogParseFailure(logger, "Response is empty", "");
            return CreateFallbackUnsure(batch, "Empty response from AI provider");
        }

        try
        {
            var cleaned = StripMarkdownCodeFences(text.Trim());
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var items = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryFindWrapperArray(root, out var wrapped))
                {
                    items.AddRange(wrapped);
                }
                else if (HasProperty(root, "verdict") || HasProperty(root, "reviewId") || HasProperty(root, "id"))
                {
                    items.Add(root);
                }
            }

            if (items.Count == 0)
            {
                LogParseFailure(logger, "No review items found in AI output", cleaned);
                return CreateFallbackUnsure(batch, "AI response contained 0 items");
            }

            var results = new List<AiReviewTriageResult>();
            for (var i = 0; i < items.Count; i++)
            {
                var parsed = ParseTriageElement(items[i], i, batch);
                if (parsed != null)
                    results.Add(parsed);
            }

            if (results.Count == 0)
            {
                LogParseFailure(logger, "No element matched the expected schema", cleaned);
                return CreateFallbackUnsure(batch, "Could not extract valid review decisions");
            }

            return results;
        }
        catch (Exception ex)
        {
            LogParseFailure(logger, $"Parsing exception: {ex.Message}", text);
            return CreateFallbackUnsure(batch, "JSON parse failure");
        }
    }

    public static IReadOnlyList<RankedAttentionItem> ParseAttentionItems(
        string text,
        IReadOnlyList<AttentionFactItem> facts,
        ILogger? logger = null)
    {
        if (facts.Count == 0) return [];
        if (string.IsNullOrWhiteSpace(text))
            return FallbackToRuleBased(facts);

        try
        {
            using var doc = JsonDocument.Parse(StripMarkdownCodeFences(text.Trim()));
            var root = doc.RootElement;

            IEnumerable<JsonElement> elements;
            if (root.ValueKind == JsonValueKind.Array)
                elements = root.EnumerateArray().ToList();
            else if (TryFindWrapperArray(root, out var wrapped))
                elements = wrapped;
            else
                elements = Array.Empty<JsonElement>();

            var ranked = new List<RankedAttentionItem>();
            foreach (var elem in elements)
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;

                var id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var fact = facts.FirstOrDefault(f => f.Id == id);
                if (fact == null) continue;

                var priority = elem.TryGetProperty("priority", out var pProp) ? pProp.GetString()?.ToLowerInvariant() ?? "medium" : "medium";
                var rank = elem.TryGetProperty("rank", out var rProp) && rProp.TryGetInt32(out var rVal) ? rVal : ranked.Count + 1;
                var headline = GetString(elem, "headline") ?? fact.FallbackHeadline;
                var headlineAr = GetString(elem, "headlineAr") ?? fact.FallbackHeadlineAr;
                var explanation = GetString(elem, "explanation") ?? fact.FallbackExplanation;
                var explanationAr = GetString(elem, "explanationAr") ?? fact.FallbackExplanationAr;

                // Every number in the text must come from the fact, otherwise use the factual template.
                var isValid = AiFactNumberValidator.ValidateNumbers(fact, headline, headlineAr, explanation, explanationAr);
                if (!isValid)
                {
                    logger?.LogWarning("Number validation failed for AI attention item {Id}. Using factual template.", fact.Id);
                    headline = fact.FallbackHeadline;
                    headlineAr = fact.FallbackHeadlineAr;
                    explanation = fact.FallbackExplanation;
                    explanationAr = fact.FallbackExplanationAr;
                }

                ranked.Add(new RankedAttentionItem(
                    fact.Id,
                    fact.Type,
                    headline,
                    headlineAr,
                    explanation,
                    explanationAr,
                    priority,
                    rank,
                    fact.ProductUrl,
                    fact.Brand,
                    fact.CompetitorName,
                    fact.LazyBeanPrice,
                    fact.CompetitorPrice,
                    IsAiGenerated: isValid));
            }

            // Facts the model omitted still appear, with the factual template.
            var includedIds = ranked.Select(r => r.Id).ToHashSet();
            foreach (var f in facts.Where(f => !includedIds.Contains(f.Id)))
            {
                ranked.Add(new RankedAttentionItem(
                    f.Id,
                    f.Type,
                    f.FallbackHeadline,
                    f.FallbackHeadlineAr,
                    f.FallbackExplanation,
                    f.FallbackExplanationAr,
                    "medium",
                    ranked.Count + 1,
                    f.ProductUrl,
                    f.Brand,
                    f.CompetitorName,
                    f.LazyBeanPrice,
                    f.CompetitorPrice,
                    IsAiGenerated: false));
            }

            return ranked.OrderBy(r => r.Rank).ToList();
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Could not parse AI attention output ({Message}). Using rule-based ranking.", ex.Message);
            return FallbackToRuleBased(facts);
        }
    }

    public static IReadOnlyList<RankedAttentionItem> FallbackToRuleBased(IReadOnlyList<AttentionFactItem> facts)
    {
        return facts.Select((f, index) => new RankedAttentionItem(
            f.Id,
            f.Type,
            f.FallbackHeadline,
            f.FallbackHeadlineAr,
            f.FallbackExplanation,
            f.FallbackExplanationAr,
            f.Type == "out_of_stock" || (f.Difference ?? 0) > 100m ? "high" : "medium",
            index + 1,
            f.ProductUrl,
            f.Brand,
            f.CompetitorName,
            f.LazyBeanPrice,
            f.CompetitorPrice,
            IsAiGenerated: false
        )).ToList();
    }

    private static AiReviewTriageResult? ParseTriageElement(JsonElement el, int index, IReadOnlyList<AiReviewTriagePairRequest> batch)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        Guid? reviewId = null;
        if (TryGetStringProperty(el, ["reviewId", "review_id", "id", "reviewid", "item_id"], out var idStr) &&
            Guid.TryParse(idStr, out var parsedGuid))
        {
            reviewId = parsedGuid;
        }

        // Positional fallback when the id is missing, malformed or index-based.
        AiReviewTriagePairRequest? matchingReq = null;
        if (reviewId.HasValue)
            matchingReq = batch.FirstOrDefault(b => b.ReviewId == reviewId.Value);

        if (matchingReq == null && index < batch.Count)
        {
            matchingReq = batch[index];
            reviewId = matchingReq.ReviewId;
        }

        if (!reviewId.HasValue || matchingReq == null)
            return null;

        var verdict = "unsure";
        if (TryGetStringProperty(el, ["verdict", "decision", "match", "status", "result"], out var vRaw))
        {
            var vLower = vRaw.Trim().ToLowerInvariant();
            if (vLower is "same" or "exact" or "match" or "identical")
                verdict = "same";
            else if (vLower is "different" or "diff" or "mismatch" or "no_match")
                verdict = "different";
        }

        var confidence = 0.5m;
        if (TryGetDecimalProperty(el, ["confidence", "score", "prob", "probability"], out var cVal))
            confidence = Math.Clamp(cVal, 0.0m, 1.0m);

        var reason = "";
        if (TryGetStringProperty(el, ["reason", "explanation", "notes", "note", "justification"], out var rRaw))
            reason = rRaw.Trim();

        return new AiReviewTriageResult(reviewId.Value, matchingReq.PairHash, verdict, confidence, reason);
    }

    private static bool TryFindWrapperArray(JsonElement root, out IEnumerable<JsonElement> items)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array &&
                WrapperArrayNames.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                items = prop.Value.EnumerateArray().ToList();
                return true;
            }
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                items = prop.Value.EnumerateArray().ToList();
                return true;
            }
        }

        items = [];
        return false;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static string StripMarkdownCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var match = Regex.Match(text, @"^```(?:json)?\s*([\s\S]*?)\s*```$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0)
                text = text[(firstNl + 1)..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
            text = text[..^3];

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
                value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
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

    private static IReadOnlyList<AiReviewTriageResult> CreateFallbackUnsure(IReadOnlyList<AiReviewTriagePairRequest> batch, string reason) =>
        batch.Select(b => new AiReviewTriageResult(b.ReviewId, b.PairHash, "unsure", 0.5m, reason)).ToList();

    private static void LogParseFailure(ILogger? logger, string errorReason, string rawContent)
    {
        if (logger == null) return;
        var sanitized = Regex.Replace(rawContent ?? "", @"key=[A-Za-z0-9_\-\.]+", "key=***");
        if (sanitized.Length > 400)
            sanitized = sanitized[..400] + "... [truncated]";
        logger.LogWarning("AI triage output could not be parsed ({Reason}). Snippet: {Snippet}", errorReason, sanitized);
    }
}
