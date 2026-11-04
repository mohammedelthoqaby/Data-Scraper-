using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Commands.RejectIdentity;

public record RejectIdentityCommand(
    Guid Id,
    string? Notes = null) : IRequest<AdvertiserIdentityDto>;
