using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Commands.ApproveIdentity;

public record ApproveIdentityCommand(
    Guid Id,
    string? Notes = null) : IRequest<AdvertiserIdentityDto>;
