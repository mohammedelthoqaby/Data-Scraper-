namespace LazyBean.Application.Ads.Dtos;

public record MetaIntegrationStatusDto(
    bool Configured,
    string State,
    double? DaysRemaining,
    DateTimeOffset? ExpiresAt,
    string? AppId,
    IReadOnlyList<string> Scopes,
    string? StatusNote,
    string? Warning);
