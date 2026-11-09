using System.Security.Cryptography;
using System.Text;
using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Matching.AiMatching;

/// <summary>
/// Cache key for an AI decision. It covers the fields the AI and the guardrails read (name, brand,
/// size, form, capsule system, roast, beverage type, category), so a pair is asked again only when
/// one of them changes. Price and URL are excluded on purpose.
/// </summary>
public static class AiPairHasher
{
    public static string Compute(MatchCandidate comp, MatchCandidate reference)
    {
        var raw = string.Join("|", KeyFields("c", comp), KeyFields("r", reference));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string KeyFields(string side, MatchCandidate c) => string.Join(";",
        side,
        Normalize(c.NormalizedTitle ?? c.Title),
        Normalize(c.Brand),
        c.WeightGrams?.ToString() ?? "",
        c.VolumeMl?.ToString() ?? "",
        c.UnitCount?.ToString() ?? "",
        Normalize(AiMatchGuardrails.FormOf(c)),
        Normalize(c.CapsuleSystem),
        Normalize(c.Roast),
        Normalize(c.BeverageType),
        c.CanonicalCategory.ToString());

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
