using LazyBean.Domain.Entities;

namespace LazyBean.Application.Collection.Orchestration;

public record CompetitorProgressDto(
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    bool IsReference,
    string Status,
    int CurrentPage,
    int? TotalPages,
    int ProductsSaved,
    DateTimeOffset LastUpdated,
    string? LastMessage);

public record PhaseProgressDto(
    string Name,
    string Status,
    string? Message,
    int? DurationMs);

public record RunProgressDto(
    Guid RunId,
    string Status,
    int OverallPercent,
    bool IsStalled,
    string? LastLogMessage,
    DateTimeOffset LastProgressAt,
    IReadOnlyList<CompetitorProgressDto> Competitors,
    IReadOnlyList<PhaseProgressDto> Phases);

public interface ICollectionProgressTracker
{
    void InitializeRun(Guid runId, IReadOnlyList<Competitor> competitors, Dictionary<Guid, int>? previousCatalogSizes = null);
    void UpdateCompetitorProgress(Guid runId, Guid competitorId, string status, int currentPage, int? totalPages, int productsSaved, string? lastMessage);
    void UpdatePhase(Guid runId, string phase, string status, string? message, int? durationMs = null);
    void SetCompleted(Guid runId, string finalStatus);
    RunProgressDto? GetProgress(Guid runId);
    RunProgressDto? GetActiveProgress();
}
