namespace LazyBean.Application.Collection.Contracts;

public record CompetitorConfigDto(
    string Slug,
    string Name,
    string BaseUrl,
    string Platform,
    string CollectionHost,
    int MinDelayMs,
    int MaxConcurrency,
    bool Enabled);
