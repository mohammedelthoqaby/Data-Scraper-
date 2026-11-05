using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Commands.DiscoverGoogleAdvertisers;

public record DiscoverGoogleAdvertisersCommand(Guid CompetitorId) : IRequest<IReadOnlyList<AdvertiserIdentityDto>>;
