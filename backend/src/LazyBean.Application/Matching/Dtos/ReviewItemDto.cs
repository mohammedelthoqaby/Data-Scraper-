namespace LazyBean.Application.Matching.Dtos;

public record ReviewQueueResponseDto(
    IReadOnlyList<ReviewMatchItemDto> Matches,
    IReadOnlyList<object> Identities,
    int TotalCount);

public record ReviewMatchItemDto(
    Guid Id,
    string SourceId,
    string Basis,
    decimal Score,
    string Status,
    ReviewProductDetailsDto? ReferenceProduct,
    ReviewProductDetailsDto? CompetitorProduct,
    DateTimeOffset CreatedAt,
    string? AiVerdict = null,
    decimal? AiConfidence = null,
    string? AiReason = null,
    string? AiProvider = null,
    string? AiSource = null);

public record ReviewProductDetailsDto(
    Guid ProductId,
    Guid VariantId,
    string Title,
    string? Vendor,
    string? Category,
    decimal Price,
    string Currency,
    string? CanonicalUrl,
    int? WeightGrams,
    int? UnitCount,
    string? ExternalId = null,
    string? CapsuleSystem = null,
    string? BeverageType = null,
    string? Brand = null,
    string? ImageUrl = null);
