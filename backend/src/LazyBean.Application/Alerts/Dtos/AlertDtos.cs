using LazyBean.Domain.Enums;

namespace LazyBean.Application.Alerts.Dtos;

public record AlertDto(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Reason,
    string? TitleAr,
    string? ReasonAr,
    string SuggestedAction,
    string? SuggestedActionAr,
    Guid? ReferenceProductId,
    string? ReferenceProductTitle,
    string? ReferenceProductUrl,
    Guid? CompetitorProductId,
    string? CompetitorProductTitle,
    string? CompetitorProductUrl,
    string? CompetitorName,
    string? AdLibraryUrl,
    bool IsBestseller,
    decimal EgpImpact,
    string MetricValuesJson,
    string Status,
    DateTimeOffset? SnoozedUntil,
    string? DismissNote,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record AlertSummaryDto(
    int TotalActive,
    int UrgentCount,
    int OpportunityCount,
    int WatchCount,
    IReadOnlyDictionary<string, int> CountsByType);

public record AlertActionRequest(
    string Action, // "done" | "snooze" | "dismiss"
    string? Note);

public record AlertHistoryDto(
    Guid Id,
    Guid AlertId,
    string Action,
    string? Note,
    string PerformedBy,
    DateTimeOffset CreatedAt);

public record AlertSettingsDto(
    decimal AdMoreExpensiveGapPercent,
    decimal PriceDropPercent,
    decimal LazyBeanAboveCheapestPercent,
    int OutOfStockCompetitorCount,
    decimal MarginRoomBelowCheapestPercent,
    int UncarriedBrandCompetitorCount,
    int BigCampaignDiscountCount,
    int ProlongedGapDays,
    decimal MaterialChangePercentPoints);

public record DataHealthDto(
    bool AllHealthy,
    int FailedRunsCount,
    IReadOnlyList<string> StaleCompetitors, // > 48h
    IReadOnlyList<string> BlockedAdCollectors,
    string MetaTokenStatus,
    DateTimeOffset? MetaTokenExpiresAt);
