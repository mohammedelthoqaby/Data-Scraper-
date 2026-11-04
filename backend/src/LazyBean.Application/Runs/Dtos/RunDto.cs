namespace LazyBean.Application.Runs.Dtos;

public record RunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<CompetitorRunDto> Competitors);

public record CompetitorRunDto(
    Guid CompetitorId,
    string CompetitorKey,
    string Status,
    string Coverage,
    int Pages,
    int Products,
    int Errors);
