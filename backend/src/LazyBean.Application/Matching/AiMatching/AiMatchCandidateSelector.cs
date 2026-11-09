using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Matching.AiMatching;

/// <summary>
/// Blocking for the AI "missed match" search. It uses the same keys as the rule matcher (brand,
/// category, beverage type, capsule system, coffee form, detected size) and ranks the survivors by
/// fuzzy name similarity.
/// </summary>
public static class AiMatchCandidateSelector
{
    public static bool CanPair(MatchCandidate competitor, MatchCandidate reference)
    {
        if (string.IsNullOrWhiteSpace(competitor.Brand) || string.IsNullOrWhiteSpace(reference.Brand))
            return false;

        if (!string.Equals(competitor.Brand, reference.Brand, StringComparison.OrdinalIgnoreCase))
            return false;

        if (competitor.CanonicalCategory != reference.CanonicalCategory)
            return false;

        if (!string.Equals(competitor.BeverageType, reference.BeverageType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(competitor.CapsuleSystem) && !string.IsNullOrEmpty(reference.CapsuleSystem) &&
            !string.Equals(competitor.CapsuleSystem, reference.CapsuleSystem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var competitorForm = ProductMatcher.ExtractCoffeeFormat(competitor);
        var referenceForm = ProductMatcher.ExtractCoffeeFormat(reference);
        if (competitorForm != null && referenceForm != null &&
            !string.Equals(competitorForm, referenceForm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasSize(competitor) && HasSize(reference);
    }

    /// <summary>Same blend the rule matcher uses for its fallback score: 70% token overlap, 30% edit distance.</summary>
    public static double Similarity(MatchCandidate competitor, MatchCandidate reference) =>
        (0.70 * TitleSimilarity.TokenDiceSimilarity(competitor.Tokens, reference.Tokens))
        + (0.30 * TitleSimilarity.NormalizedLevenshtein(competitor.NormalizedTitle, reference.NormalizedTitle));

    public static IReadOnlyList<MatchCandidate> TopCandidates(
        MatchCandidate reference,
        IEnumerable<MatchCandidate> competitorProducts,
        int limit)
    {
        return competitorProducts
            .Where(c => CanPair(c, reference))
            .OrderByDescending(c => Similarity(c, reference))
            .Take(limit)
            .ToList();
    }

    private static bool HasSize(MatchCandidate candidate) =>
        candidate.WeightGrams.HasValue || candidate.VolumeMl.HasValue || candidate.UnitCount.HasValue;
}
