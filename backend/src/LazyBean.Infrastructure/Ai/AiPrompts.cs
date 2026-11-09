using System.Text.Json;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;

namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Compact prompts for the routed providers. Both prompts ask for a single JSON object
/// (JSON object mode on Groq rejects top-level arrays), and keep the payload small to
/// stay inside the free-tier tokens-per-minute budget.
/// </summary>
internal static class AiPrompts
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string TriageSystem =
        "You compare product listings from Lazy Bean (reference) and a competitor, both coffee retailers in Egypt. " +
        "Decide whether each pair is the EXACT same product. " +
        "same = same brand, line/flavour/variant, weight or unit count, format and capsule system (naming or language differences such as 'Ground' vs 'مطحون' are fine). " +
        "different = differs in origin or flavour line, roast, size, pack count or capsule compatibility. " +
        "unsure = critical details are missing or ambiguous. " +
        "Reply with one JSON object: {\"results\":[{\"reviewId\":\"<id>\",\"verdict\":\"same|different|unsure\",\"confidence\":0.0-1.0,\"reason\":\"<English, max 120 chars>\"}]} " +
        "with one entry per input pair.";

    private const string AttentionSystem =
        "You rank commercial attention items for the Lazy Bean coffee dashboard in Egypt, using only the facts provided. " +
        "Priority is high, medium or low. Give an English and an Arabic headline and explanation. " +
        "CRITICAL: every number in a headline or explanation must appear exactly in that item's allowedNumbers; never invent or round numbers. " +
        "Reply with one JSON object: {\"items\":[{\"id\":\"<fact id>\",\"priority\":\"high|medium|low\",\"rank\":1,\"headline\":\"\",\"headlineAr\":\"\",\"explanation\":\"\",\"explanationAr\":\"\"}]}.";

    public static (string System, string User) BuildTriage(IReadOnlyList<AiReviewTriagePairRequest> batch)
    {
        var pairs = batch.Select(p => new
        {
            reviewId = p.ReviewId,
            reference = new
            {
                title = p.RefTitle,
                brand = p.RefBrand,
                category = p.RefCategory,
                grams = p.RefWeightGrams,
                units = p.RefUnitCount,
                capsule = p.RefCapsuleSystem,
                format = p.RefFormat,
            },
            competitor = new
            {
                title = p.CompTitle,
                brand = p.CompBrand,
                category = p.CompCategory,
                grams = p.CompWeightGrams,
                units = p.CompUnitCount,
                capsule = p.CompCapsuleSystem,
                format = p.CompFormat,
            },
        });

        return (TriageSystem, "Pairs:\n" + JsonSerializer.Serialize(pairs, CompactOptions));
    }

    public static (string System, string User) BuildAttention(IReadOnlyList<AttentionFactItem> facts)
    {
        var payload = facts.Select(f => new
        {
            id = f.Id,
            type = f.Type,
            productTitle = f.ProductTitle,
            brand = f.Brand,
            competitorName = f.CompetitorName,
            lazyBeanPrice = f.LazyBeanPrice,
            competitorPrice = f.CompetitorPrice,
            difference = f.Difference,
            discountPercent = f.DiscountPercent,
            adCount = f.AdCount,
            allowedNumbers = f.Numbers,
        });

        return (AttentionSystem, "Facts:\n" + JsonSerializer.Serialize(payload, CompactOptions));
    }
}
