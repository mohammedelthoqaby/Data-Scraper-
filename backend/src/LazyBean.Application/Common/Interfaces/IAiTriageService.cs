namespace LazyBean.Application.Common.Interfaces;

public record AiTriageStats(
    int TotalEvaluated,
    int CacheHits,
    int ApiCallsMade,
    int SameCount,
    int DifferentCount,
    int UnsureCount);

public interface IAiTriageService
{
    string ComputePairHash(
        string refTitle,
        string? refBrand,
        string? refCategory,
        int? refWeight,
        int? refUnit,
        string? refCapsule,
        string compTitle,
        string? compBrand,
        string? compCategory,
        int? compWeight,
        int? compUnit,
        string? compCapsule);

    Task<AiTriageStats> TriagePendingReviewsAsync(int limit = 100, CancellationToken ct = default, Guid? runId = null);
}
