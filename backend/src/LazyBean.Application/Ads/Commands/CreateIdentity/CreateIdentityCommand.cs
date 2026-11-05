using LazyBean.Application.Ads.Dtos;
using MediatR;

namespace LazyBean.Application.Ads.Commands.CreateIdentity;

public record CreateIdentityCommand(
    Guid CompetitorId,
    string Platform,
    string ExternalId,
    string? Name = null,
    string? LibraryUrl = null,
    string? OwnershipEvidenceUrl = null,
    string? Notes = null) : IRequest<AdvertiserIdentityDto>;
