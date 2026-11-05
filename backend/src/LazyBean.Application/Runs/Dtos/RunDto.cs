namespace LazyBean.Application.Runs.Dtos;

public record RunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<CompetitorRunDto> Competitors,
    int? MatchesCount = null,
    string? PhasesJson = null);

public record CompetitorRunDto(
    Guid CompetitorId,
    string CompetitorKey,
    string? CompetitorName,
    bool IsReference,
    string Status,
    string Coverage,
    int Pages,
    int Products,
    int Errors)
{
    public CompetitorRunDto(
        Guid competitorId,
        string competitorKey,
        string status,
        string coverage,
        int pages,
        int products,
        int errors)
        : this(competitorId, competitorKey, competitorKey, false, status, coverage, pages, products, errors)
    {
    }
}
