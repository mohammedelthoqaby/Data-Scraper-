using System.Text.RegularExpressions;
using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Matching.AiMatching;

public sealed record GuardrailVerdict(bool Blocked, string? Reason, bool IsBundle);

/// <summary>
/// Hard rules that override any AI verdict. They run before the AI is asked, so a pair that fails
/// them costs no request and can never become a match.
/// </summary>
public static class AiMatchGuardrails
{
    private const double WeightTolerance = 0.02;

    private static readonly Regex BundleWords = new(
        @"\b(bundle|combo|gift|multi\s*pack|multipack|value\s*pack|set\s+of|pack\s+of|\d+\s*x\s*\d+|\d+\s*pcs?)\b|\+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DripRegex = new(@"\b(drip|sachet|bag)s?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SyrupRegex = new(@"\b(syrup|sauce)\b|سيروب", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstantRegex = new(@"\binstant\b|سريع الذوبان", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GuardrailVerdict Evaluate(MatchCandidate comp, MatchCandidate reference)
    {
        if (!string.Equals(comp.BeverageType, reference.BeverageType, StringComparison.OrdinalIgnoreCase))
            return Block($"different beverage type {comp.BeverageType} vs {reference.BeverageType}");

        if (!string.IsNullOrEmpty(comp.CapsuleSystem) && !string.IsNullOrEmpty(reference.CapsuleSystem) &&
            !string.Equals(comp.CapsuleSystem, reference.CapsuleSystem, StringComparison.OrdinalIgnoreCase))
        {
            return Block($"different capsule system {comp.CapsuleSystem} vs {reference.CapsuleSystem}");
        }

        var compForm = FormOf(comp);
        var refForm = FormOf(reference);
        if (compForm != null && refForm != null && !string.Equals(compForm, refForm, StringComparison.OrdinalIgnoreCase))
            return Block($"different form {compForm} vs {refForm}");

        if (comp.WeightGrams is { } cw && reference.WeightGrams is { } rw && !WithinTolerance(cw, rw))
            return Block($"different size {cw}g vs {rw}g");

        if (comp.VolumeMl is { } cv && reference.VolumeMl is { } rv && !WithinTolerance(cv, rv))
            return Block($"different volume {cv}ml vs {rv}ml");

        if (comp.UnitCount is { } cu && reference.UnitCount is { } ru && cu != ru)
            return Block($"different pack size {cu} vs {ru}");

        var isBundle = IsBundle(comp) || IsBundle(reference);
        if (isBundle && (UnitPrice(comp) is null || UnitPrice(reference) is null))
            return Block("bundle or multipack: unit price cannot be normalized");

        return new GuardrailVerdict(false, null, isBundle);
    }

    /// <summary>Coarse physical form: beans, ground, capsules, drip, syrup or instant. Null when unknown.</summary>
    public static string? FormOf(MatchCandidate candidate)
    {
        if (!string.IsNullOrEmpty(candidate.CapsuleSystem))
            return "capsules";

        var coffeeFormat = ProductMatcher.ExtractCoffeeFormat(candidate);
        if (coffeeFormat != null)
            return coffeeFormat.ToLowerInvariant();

        var title = candidate.Title ?? string.Empty;
        if (DripRegex.IsMatch(title)) return "drip";
        if (SyrupRegex.IsMatch(title)) return "syrup";
        if (InstantRegex.IsMatch(title)) return "instant";
        return null;
    }

    public static bool IsBundle(MatchCandidate candidate) =>
        (candidate.UnitCount ?? 1) > 1 || BundleWords.IsMatch(candidate.Title ?? string.Empty);

    /// <summary>Price per gram when a weight is known, otherwise price per unit. Null when neither is available.</summary>
    public static decimal? UnitPrice(MatchCandidate candidate)
    {
        if (candidate.Price <= 0)
            return null;

        if (candidate.WeightGrams is > 0)
            return candidate.Price / candidate.WeightGrams.Value;

        if (candidate.UnitCount is > 0)
            return candidate.Price / candidate.UnitCount.Value;

        return null;
    }

    private static bool WithinTolerance(int a, int b)
    {
        var max = Math.Max(a, b);
        return max > 0 && (double)Math.Abs(a - b) / max <= WeightTolerance;
    }

    private static GuardrailVerdict Block(string reason) => new(true, reason, false);
}
