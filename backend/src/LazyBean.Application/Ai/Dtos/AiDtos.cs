namespace LazyBean.Application.Ai.Dtos;

/// <summary>Usage and status for one provider. Contains no key material.</summary>
public record AiProviderUsageDto(
    DateOnly UsageDate,
    int RequestsToday,
    long TokensToday,
    int DailyRequestLimit,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset? Last429At);

public record AiProviderStatusDto(
    string Provider,
    string Label,
    bool IsConfigured,
    string KeySource, // "none" | "database" | "environment"
    string? MaskedApiKey,
    string ModelName,
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels,
    DateTimeOffset? LastTestedAt,
    string? LastTestStatus,
    string? LastTestMessage,
    AiProviderUsageDto Usage);

public record AiSettingsDto(
    bool IsEnabled,
    string PrimaryProvider,
    int DailyRequestCap,
    int TriageRunCap,
    IReadOnlyList<AiProviderStatusDto> Providers,
    bool CreativeVisionEnabled = false,
    int CreativeVisionDailyCap = 20,
    int CreativeVisionUsedToday = 0);

public record UpdateAiSettingsRequestDto(
    bool? IsEnabled,
    string? PrimaryProvider,
    int? DailyRequestCap,
    int? TriageRunCap,
    bool? CreativeVisionEnabled = null,
    int? CreativeVisionDailyCap = null);

public record SaveAiProviderRequestDto(
    string? ApiKey,
    string? ModelName);

public record TestAiProviderRequestDto(
    string? ApiKey,
    string? ModelName);

/// <summary>Outcome of a key test. Status: "ok" | "invalid_key" | "rate_limited" | "error" | "not_configured".</summary>
public record AiKeyTestResultDto(
    string Status,
    string Message);

/// <summary>Provider-agnostic JSON-mode request. Output must be a single JSON object.</summary>
public record AiJsonRequest(
    string SystemPrompt,
    string UserPrompt,
    int MaxOutputTokens);

public record AiCompletionResult(
    string Provider,
    string Model,
    string Text,
    int TotalTokens);

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
    string Reason,
    string? Model = null); // model that produced the verdict, when known

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
