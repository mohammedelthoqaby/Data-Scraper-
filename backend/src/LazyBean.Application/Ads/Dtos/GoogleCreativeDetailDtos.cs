namespace LazyBean.Application.Ads.Dtos;

// What one GetCreativeById response says about a Google creative. Every field is optional: a response does not carry all of
// them for every creative type, so a missing field is null (or an empty list).
public sealed record GoogleCreativeDetailDto(
    string ExternalAdId,
    string? Headline,
    string? Description,
    string? LandingUrl,
    string? PreviewUrl,
    IReadOnlyList<string> Variations,
    DateTimeOffset? LastShownAt);

// The details the desktop app read for one advertiser, sent to the server.
public sealed record GoogleCreativeDetailsRequest(string AdvertiserId, IReadOnlyList<GoogleCreativeDetailDto> Items);

// Updated: creatives whose details were stored. NotFound: creatives the server does not have for this advertiser.
public sealed record GoogleCreativeDetailsResponse(int Updated, int NotFound);
