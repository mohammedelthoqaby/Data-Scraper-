using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Commands.RejectIdentity;

public class RejectIdentityCommandHandler(IUnitOfWork unitOfWork, IReadDbContext readContext)
    : IRequestHandler<RejectIdentityCommand, AdvertiserIdentityDto>
{
    public async Task<AdvertiserIdentityDto> Handle(RejectIdentityCommand request, CancellationToken ct)
    {
        var identity = await unitOfWork.AdvertiserIdentities.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException(nameof(AdvertiserIdentity), request.Id);

        identity.Reject(request.Notes);
        unitOfWork.AdvertiserIdentities.Update(identity);
        await unitOfWork.SaveChangesAsync(ct);

        var comp = await readContext.Query<Competitor>()
            .Where(c => c.Id == identity.CompetitorId)
            .FirstOrDefaultAsync(ct);

        return new AdvertiserIdentityDto(
            identity.Id,
            identity.CompetitorId,
            comp?.Key ?? "",
            comp?.Name ?? "",
            identity.Platform.ToString(),
            identity.ExternalId,
            identity.Name,
            identity.Status.ToString(),
            identity.LibraryUrl,
            identity.OwnershipEvidenceUrl,
            identity.Notes,
            identity.ReviewedAt,
            identity.CreatedAt);
    }
}
