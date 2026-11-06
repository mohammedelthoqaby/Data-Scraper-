namespace LazyBean.Application.Ads.Dtos;

// Status is one of: "found" (new candidates this run), "none" (Google returned no advertiser),
// "blocked" (Google rate-limited or refused the request), "error" (any other failure).
public record GoogleDiscoveryResultDto(
    string Status,
    int CandidatesFound,
    string? Message,
    IReadOnlyList<AdvertiserIdentityDto> Identities);
