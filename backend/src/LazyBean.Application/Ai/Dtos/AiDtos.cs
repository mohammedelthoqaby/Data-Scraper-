namespace LazyBean.Application.Ai.Dtos;

public record AiSettingsDto(
    bool IsEnabled,
    string ModelName,
    string? MaskedApiKey,
    int DailyRequestCap,
    int RequestsUsedToday,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSuccess,
    string? LastTestMessage,
    DateTimeOffset? LastCallAt = null,
    string? LastCallStatus = null);

public record UpdateAiSettingsRequestDto(
    bool? IsEnabled,
    string? ApiKey,
    string? ModelName,
    int? DailyRequestCap);

public record TestAiConnectionRequestDto(
    string? ApiKey,
    string? ModelName);

public record AiTestResultDto(
    bool Success,
    string Message);

public record AiReviewTriagePairRequest(
    Guid ReviewId,
    string RefTitle,
    string? RefBrand,
    string? RefCategory,
    int? RefWeightGrams,
    int? RefUnitCount,
    string? RefCapsuleSystem,
    string? RefFormat,
    string CompTitle,
    string? CompBrand,
    string? CompCategory,
    int? CompWeightGrams,
    int? CompUnitCount,
    string? CompCapsuleSystem,
    string? CompFormat,
    string PairHash = "")
{
    public AiReviewTriagePairRequest(
        Guid reviewId,
        string refTitle,
        string compTitle,
        string? refBrand = null,
        string? compBrand = null,
        int? refWeightGrams = null,
        int? compWeightGrams = null)
        : this(
            reviewId,
            refTitle,
            refBrand,
            null,
            refWeightGrams,
            null,
            null,
            null,
            compTitle,
            compBrand,
            null,
            compWeightGrams,
            null,
            null,
            null,
            "")
    {
    }
}

public record AiReviewTriageResult(
    Guid ReviewId,
    string PairHash,
    string Verdict, // "same" | "different" | "unsure"
    decimal Confidence, // 0.0 - 1.0
    string Reason);

public record RankedAttentionItem(
    string Id,
    string Type,
    string Headline,
    string HeadlineAr,
    string Explanation,
    string ExplanationAr,
    string Priority, // "high" | "medium" | "low"
    int Rank,
    string? ProductUrl,
    string? Brand,
    string CompetitorName,
    decimal LazyBeanPrice,
    decimal CompetitorPrice,
    bool IsAiGenerated);

public record AttentionFactItem(
    string Id,
    string Type,
    string ProductTitle,
    string? Brand,
    string CompetitorName,
    decimal LazyBeanPrice,
    decimal CompetitorPrice,
    decimal? Difference,
    decimal? DiscountPercent,
    int? AdCount,
    string? ProductUrl,
    IReadOnlyList<decimal> Numbers,
    string FallbackHeadline,
    string FallbackExplanation,
    string FallbackHeadlineAr,
    string FallbackExplanationAr);
