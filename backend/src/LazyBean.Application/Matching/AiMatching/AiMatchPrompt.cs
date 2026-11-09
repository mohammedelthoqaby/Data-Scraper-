using System.Text.Json;
using System.Text.Json.Serialization;
using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Matching.AiMatching;

/// <summary>One numbered comparison inside a batch. Reference is the Lazy Bean product, Competitor the other store's.</summary>
public sealed record AiMatchPairInput(int Pair, MatchCandidate Reference, MatchCandidate Competitor);

/// <summary>
/// Compact prompt for product matching. Only the fields that decide identity are sent:
/// name, brand, size, form, roast, price and the URL handle.
/// </summary>
public static class AiMatchPrompt
{
    private const string SystemPrompt =
        "You decide whether two beverage listings from two retailers (Lazy Bean and a competitor, Egypt) are the SAME product. " +
        "Same means same brand, product line and flavour or roast, same size and pack count, same form and same capsule system. " +
        "Ignore wording, language (Arabic or English), punctuation and store names. " +
        "Any difference in size, pack count, form (beans, ground, capsules, drip, syrup), capsule system or flavour line means different. " +
        "Answer unsure only when the data is too thin to decide. " +
        "Reply with one JSON object only: " +
        "{\"results\":[{\"pair\":<number>,\"verdict\":\"same|different|unsure\",\"confidence\":<0.0-1.0>,\"reason\":\"<English, max 100 chars, e.g. different size 250g vs 1kg>\"}]} " +
        "with one entry per pair.";

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static (string System, string User) Build(IReadOnlyList<AiMatchPairInput> pairs)
    {
        var payload = pairs.Select(p => new
        {
            pair = p.Pair,
            lazyBean = Describe(p.Reference),
            competitor = Describe(p.Competitor),
        });

        return (SystemPrompt, "Pairs:\n" + JsonSerializer.Serialize(payload, CompactOptions));
    }

    public static object Describe(MatchCandidate candidate) => new
    {
        name = Truncate(candidate.Title, 160),
        brand = candidate.Brand,
        size = SizeText(candidate),
        form = AiMatchGuardrails.FormOf(candidate),
        roast = candidate.Roast,
        price = candidate.Price,
        handle = Handle(candidate.CanonicalUrl),
    };

    public static string SizeText(MatchCandidate candidate)
    {
        var parts = new List<string>();
        if (candidate.WeightGrams is { } grams) parts.Add($"{grams}g");
        if (candidate.VolumeMl is { } ml) parts.Add($"{ml}ml");
        if (candidate.UnitCount is { } units) parts.Add($"{units} pcs");
        return string.Join(" + ", parts);
    }

    /// <summary>The last path segment of a product URL, for example "antica-bottega-250g".</summary>
    public static string? Handle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(segment) ? null : Truncate(segment, 120);
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
