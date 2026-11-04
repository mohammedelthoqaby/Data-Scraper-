using MediatR;

namespace LazyBean.Application.Offers.Queries.GetReferenceOffers;

public sealed record GetReferenceOffersQuery(Guid? RunId = null) : IRequest<ReferenceOffersResponseDto>;

public sealed record ReferenceOffersResponseDto(
    Guid? RunId,
    int TotalOffers,
    IReadOnlyList<ReferenceOfferDto> Offers);

public sealed record ReferenceOfferDto(
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
