namespace LazyBean.Application.Matching;

public record MatchingResult(
    int AutoAcceptedCount,
    int ReviewItemCount,
    IReadOnlyDictionary<string, int> MatchesPerCompetitor,
    int SizeVariantCount = 0,
    IReadOnlyDictionary<string, int>? SizeVariantsPerCompetitor = null);

public interface IProductMatcher
{
    Task<MatchingResult> MatchAllAsync(CancellationToken ct = default);
    Task<MatchingResult> MatchAllAsync(Guid? runId, CancellationToken ct = default);
}
