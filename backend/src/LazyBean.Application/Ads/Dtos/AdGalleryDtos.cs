namespace LazyBean.Application.Ads.Dtos;

// One card of the ads gallery: the newest ad of a group of identical ads (same store, same text, same first image).
// Ids, creative IDs and ingest counts are not on the card; the side panel has the variations.
public sealed record AdGalleryCardDto(
    Guid Id,
    Guid CompetitorId,
    string CompetitorName,
    bool IsOurs,
    string Platform,
    string Status,
    string? Headline,
    string? Body,
    string? CreativeFormat,
    int ThumbnailCount,
    int VariationCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? StoreUrl,
    decimal? AdvertisedPrice,
    int? AdvertisedQuantity,
    decimal? UnitPrice,
    Guid? LinkedProductId,
    string? LinkedProductTitle,
    Guid? OurProductId,
    string? OurProductTitle,
    decimal? OurPrice,
    decimal? CompetitorPrice,
    decimal? OurPriceGapPercent,
    // The fingerprint of the card image. It changes with the image, so the image address can change with it.
    string? ThumbnailSha256 = null);

// TotalCount: the cards. TotalAds: the ads behind them (a card can stand for several identical ads).
public sealed record AdGalleryResponseDto(int TotalCount, int TotalAds, int Page, int PageSize, IReadOnlyList<AdGalleryCardDto> Items);

// One ad of a card's group, for the side panel.
public sealed record AdGalleryVariationDto(
    Guid Id,
    string? Headline,
    string? Body,
    string? LinkUrl,
    string? StoreUrl,
    string Status,
    string? CreativeFormat,
    int ThumbnailCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record AdGalleryDetailDto(AdGalleryCardDto Card, IReadOnlyList<AdGalleryVariationDto> Variations);
