using MediatR;

namespace LazyBean.Application.Ads.Commands.SyncAds;

public record SyncAdsCommand(
    Guid? CompetitorId = null,
    string? Platform = null) : IRequest<SyncAdsResultDto>;

public record SyncAdsResultDto(
    int IdentitiesProcessed,
    int TotalAdsFetched,
    int NewAdsCount,
    int ActiveAdsCount,
    int CampaignsCreated,
    IReadOnlyList<string> Errors);
