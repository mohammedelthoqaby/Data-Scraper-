using FluentValidation.Results;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Commands.CreateIdentity;

public class CreateIdentityCommandHandler(IUnitOfWork unitOfWork, IReadDbContext readContext)
    : IRequestHandler<CreateIdentityCommand, AdvertiserIdentityDto>
{
    public async Task<AdvertiserIdentityDto> Handle(CreateIdentityCommand request, CancellationToken ct)
    {
        var competitor = await unitOfWork.Competitors.GetByIdAsync(request.CompetitorId, ct)
            ?? throw new NotFoundException(nameof(Competitor), request.CompetitorId);

        if (!Enum.TryParse<AdPlatform>(request.Platform, true, out var platform))
        {
            throw new ValidationException([new ValidationFailure("Platform", $"Invalid AdPlatform '{request.Platform}'. Must be 'Meta' or 'Google'.")]);
        }

        var existing = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == platform && i.ExternalId == request.ExternalId, ct);

        if (existing != null)
        {
            return new AdvertiserIdentityDto(
                existing.Id,
                competitor.Id,
                competitor.Key,
                competitor.Name,
                existing.Platform.ToString(),
                existing.ExternalId,
                existing.Name,
                existing.Status.ToString(),
                existing.LibraryUrl,
                existing.OwnershipEvidenceUrl,
                existing.Notes,
                existing.ReviewedAt,
                existing.CreatedAt);
        }

        var identity = new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = platform,
            ExternalId = request.ExternalId.Trim(),
            Name = request.Name.Trim(),
            Status = AdvertiserIdentityStatus.Pending,
            LibraryUrl = request.LibraryUrl,
            OwnershipEvidenceUrl = request.OwnershipEvidenceUrl,
            Notes = request.Notes
        };

        await unitOfWork.AdvertiserIdentities.AddAsync(identity, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new AdvertiserIdentityDto(
            identity.Id,
            competitor.Id,
            competitor.Key,
            competitor.Name,
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
