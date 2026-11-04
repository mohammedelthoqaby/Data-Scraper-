namespace LazyBean.Application.Competitors.Dtos;

public record CompetitorDto(
    Guid Id,
    string Key,
    string Name,
    string BaseUrl,
    string Platform,
    bool IsReference,
    bool Enabled,
    int MinDelayMs = 3000,
    int MaxConcurrency = 1);
