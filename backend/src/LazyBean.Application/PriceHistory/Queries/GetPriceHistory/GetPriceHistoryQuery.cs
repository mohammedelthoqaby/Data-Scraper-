using MediatR;

namespace LazyBean.Application.PriceHistory.Queries.GetPriceHistory;

public sealed record GetPriceHistoryQuery(
    Guid? ProductId = null,
    Guid? VariantId = null,
    Guid? CompetitorId = null,
    string? CompetitorKey = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50,
    bool All = false) : IRequest<PriceHistoryResponseDto>;

public sealed record PriceHistoryResponseDto(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<PriceHistoryPointDto> Items);

public sealed record PriceHistoryPointDto(
    Guid ObservationId,
    DateTimeOffset ObservedAt,
    Guid RunId,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    Guid ProductId,
    string ProductTitle,
    string? ExternalId,
    Guid VariantId,
    string? VariantTitle,
    decimal Price,
    decimal? CompareAtPrice,
    bool? InStock,
    string Currency,
    string? ProductUrl = null);
