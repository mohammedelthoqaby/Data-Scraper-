using MediatR;

namespace LazyBean.Application.Runs.Queries.GetPriceMatrix;

public sealed record GetPriceMatrixQuery(Guid? RunId = null) : IRequest<PriceMatrixResponseDto>;

public sealed record PriceMatrixResponseDto(
    Guid? RunId,
    DateTimeOffset GeneratedAt,
    int TotalRows,
    IReadOnlyList<PriceMatrixRowDto> Rows);

public sealed record PriceMatrixRowDto(
    Guid LazyBeanProductId,
    Guid LazyBeanVariantId,
    string LazyBeanExternalId,
    string LazyBeanTitle,
    string? Category,
    string CanonicalCategory,
    string? Brand,
    int? WeightGrams,
    int? UnitCount,
    int? VolumeMl,
    string LazyBeanUrl,
    decimal LazyBeanPrice,
    string? LazyBeanVendor,
    int LazyBeanVendorCount,
    bool LazyBeanInStock,
    string? CheapestCompetitorKey,
    string? CheapestCompetitorName,
    decimal? CheapestCompetitorPrice,
    string Position,
    IReadOnlyDictionary<string, CompetitorPriceCellDto> CompetitorPrices);

public sealed record CompetitorPriceCellDto(
    string CompetitorName,
    string CompetitorKey,
    Guid CompetitorProductId,
    Guid CompetitorVariantId,
    string CompetitorTitle,
    string CompetitorUrl,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? PricePerGram,
    decimal? PricePerUnit,
    bool InStock,
    string MatchStatus,
    decimal DifferenceVsLazyBean,
    decimal? DifferencePercentageVsLazyBean,
    DateTimeOffset ObservedAt,
    bool IsStale,
    string? Brand = null,
    string? Vendor = null,
    Guid? MatchId = null,
    string? MatchMethod = null,
    string? MatchReason = null,
    bool IsBundle = false);
