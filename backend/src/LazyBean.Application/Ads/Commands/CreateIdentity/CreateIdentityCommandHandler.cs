using FluentValidation.Results;
using LazyBean.Application.Ads.Common;
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

        var cleanExtId = platform == AdPlatform.Google
            ? ParseGoogleAdvertiserId(request.ExternalId)
            : request.ExternalId.Trim();

        var existing = await readContext.Query<AdvertiserIdentity>()
            .FirstOrDefaultAsync(i => i.Platform == platform && i.ExternalId == cleanExtId, ct);

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

        var displayName = string.IsNullOrWhiteSpace(request.Name) ? competitor.Name : request.Name.Trim();
        var libraryUrl = request.LibraryUrl;
        if (string.IsNullOrWhiteSpace(libraryUrl))
        {
            libraryUrl = platform == AdPlatform.Meta
                ? $"https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=EG&view_all_page_id={cleanExtId}"
                : $"https://adstransparency.google.com/advertiser/{cleanExtId}?region=EG";
        }

        var identity = new AdvertiserIdentity
        {
            CompetitorId = competitor.Id,
            Platform = platform,
            ExternalId = cleanExtId,
            Name = displayName,
            Status = AdvertiserIdentityStatus.Pending,
            LibraryUrl = libraryUrl,
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

    private static string ParseGoogleAdvertiserId(string? input)
    {
        if (!GoogleAdvertiserReference.TryParse(input, out var advertiserId))
        {
            throw new ValidationException([new ValidationFailure("ExternalId", GoogleAdvertiserReference.InvalidMessage)]);
        }

        return advertiserId;
    }
}
