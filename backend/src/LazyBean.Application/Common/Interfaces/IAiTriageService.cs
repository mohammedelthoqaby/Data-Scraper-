namespace LazyBean.Application.Common.Interfaces;

public record AiTriageStats(
    int TotalEvaluated,
    int CacheHits,
    int ApiCallsMade,
    int SameCount,
    int DifferentCount,
    int UnsureCount,
    int Deferred = 0,
    string? StopReason = null);

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

    /// <summary>
    /// Triages pending product-match reviews. <paramref name="limit"/> is capped by the
    /// configured per-run cap (AiSetting.TriageRunCap, default 200); null uses the cap.
    /// Never throws for AI unavailability: unprocessed items stay pending and are reported in Deferred.
    /// </summary>
    Task<AiTriageStats> TriagePendingReviewsAsync(int? limit = null, CancellationToken ct = default, Guid? runId = null);
}
