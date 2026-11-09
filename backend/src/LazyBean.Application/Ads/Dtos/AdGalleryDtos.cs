namespace LazyBean.Application.Ads.Dtos;

// One card of the ads gallery: the newest ad of a group of identical ads (same store, same text, same first image).
// Ids, creative IDs and ingest counts are not on the card; the ad window has them (see AdGalleryDetailDto).
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
// Every card that matches the filters, in one response. Truncated is true when the filters matched more ads than
// the gallery reads at once (AdGalleryQueries.MaxCandidates); narrowing the filters shows the rest.
public sealed record AdGalleryResponseDto(int TotalCount, int TotalAds, bool Truncated, IReadOnlyList<AdGalleryCardDto> Items);

// One ad of a card's group, for the ad window.
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

// What the detail adds to the card, for the ad window: the ad's own ID, the advertiser or page ID it was bought under, why an
// unlinked ad has no product (AdLinkReasons code, null when it is linked), and when delivery started and stopped (Meta).
public sealed record AdGalleryFactsDto(
    string ExternalAdId,
    string? AdvertiserId,
    string? LinkReason,
    DateTimeOffset? DeliveryStartAt,
    DateTimeOffset? DeliveryStopAt,
    // The platforms a Meta ad ran on (facebook, instagram, ...). Empty when not recorded, and for Google ads.
    IReadOnlyList<string> Placements);

public sealed record AdGalleryDetailDto(
    AdGalleryCardDto Card,
    IReadOnlyList<AdGalleryVariationDto> Variations,
    AdGalleryFactsDto Facts);
