namespace LazyBean.Application.Ads.Dtos;

public record AdvertiserIdentityDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    string Platform,
    string ExternalId,
    string Name,
    string Status,
    string? LibraryUrl,
    string? OwnershipEvidenceUrl,
    string? Notes,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt);
