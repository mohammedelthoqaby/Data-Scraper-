using MediatR;

namespace LazyBean.Application.Runs.Queries.GetCompetitorOffers;

public sealed record GetCompetitorOffersQuery(
    Guid RunId,
    Guid? CompetitorId = null,
    string? CompetitorKey = null,
    string? Status = null) : IRequest<CompetitorOffersResponseDto>;

public sealed record CompetitorOffersResponseDto(
    Guid RunId,
    int TotalOffers,
    IReadOnlyList<CompetitorOfferDto> Offers,
    IReadOnlyDictionary<string, int>? CountsByStatus = null);

public sealed record CompetitorOfferDto(
    Guid OfferId,
    Guid CompetitorId,
    string CompetitorKey,
    string CompetitorName,
    Guid? ProductId,
    string ProductTitle,
    string CanonicalUrl,
    string OfferType,
    decimal Price,
    decimal CompareAtPrice,
    decimal DiscountAmount,
    decimal DiscountPercentage,
    string Currency,
    DateTimeOffset CreatedAt,
    string Status,
    string? Brand = null,
    string? Vendor = null);
