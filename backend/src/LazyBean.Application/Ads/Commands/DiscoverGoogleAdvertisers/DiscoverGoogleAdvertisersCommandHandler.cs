using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Ads.Commands.DiscoverGoogleAdvertisers;

public class DiscoverGoogleAdvertisersCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    IGoogleAdsTransparencyClient googleClient,
    ILogger<DiscoverGoogleAdvertisersCommandHandler> logger)
    : IRequestHandler<DiscoverGoogleAdvertisersCommand, IReadOnlyList<AdvertiserIdentityDto>>
{
    public async Task<IReadOnlyList<AdvertiserIdentityDto>> Handle(DiscoverGoogleAdvertisersCommand request, CancellationToken ct)
    {
        var competitor = await unitOfWork.Competitors.GetByIdAsync(request.CompetitorId, ct)
            ?? throw new NotFoundException(nameof(Competitor), request.CompetitorId);

        var queries = new List<string> { competitor.Name };
        if (!string.IsNullOrWhiteSpace(competitor.BaseUrl) && Uri.TryCreate(competitor.BaseUrl, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                host = host[4..];
            }

            if (!queries.Any(q => string.Equals(q, host, StringComparison.OrdinalIgnoreCase)))
            {
                queries.Add(host);
            }
        }

        var allCandidates = new Dictionary<string, GoogleAdvertiserCandidateDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var q in queries)
        {
            try
            {
                var candidates = await googleClient.SearchCandidatesAsync(q, ct);
                foreach (var c in candidates)
                {
                    allCandidates.TryAdd(c.AdvertiserId, c);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to search Google Ads Transparency candidates for competitor '{Name}' with query '{Query}'.", competitor.Name, q);
            }
        }

        // Idempotently create AdvertiserIdentity in Pending status for each candidate
        var existingIdentities = await readContext.Query<AdvertiserIdentity>()
            .Where(i => i.CompetitorId == competitor.Id && i.Platform == AdPlatform.Google)
            .ToListAsync(ct);
        var existingByExtId = existingIdentities.ToDictionary(i => i.ExternalId, StringComparer.OrdinalIgnoreCase);

        var createdOrExisting = new List<AdvertiserIdentity>();
        var hasNew = false;

        foreach (var (advId, candidate) in allCandidates)
        {
            if (existingByExtId.TryGetValue(advId, out var existing))
            {
                createdOrExisting.Add(existing);
                continue;
            }

            var identity = new AdvertiserIdentity
            {
                CompetitorId = competitor.Id,
                Platform = AdPlatform.Google,
                ExternalId = advId,
                Name = string.IsNullOrWhiteSpace(candidate.Name) ? competitor.Name : candidate.Name,
                Status = AdvertiserIdentityStatus.Pending,
                LibraryUrl = $"https://adstransparency.google.com/advertiser/{advId}?region=EG",
                Notes = $"Discovered via Google Ads Transparency (Region: {candidate.Region ?? "EG"}, Approx Ads: {candidate.ApproxAdCount?.ToString() ?? "N/A"})"
            };

            await unitOfWork.AdvertiserIdentities.AddAsync(identity, ct);
            createdOrExisting.Add(identity);
            hasNew = true;
        }

        if (hasNew)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Discovered and saved {Count} new Google advertiser identities for competitor '{Name}'.", createdOrExisting.Count - existingIdentities.Count, competitor.Name);
        }

        // Return all identities for this competitor (both existing and newly created)
        var allCompetitorGoogleIdentities = await readContext.Query<AdvertiserIdentity>()
            .Where(i => i.CompetitorId == competitor.Id && i.Platform == AdPlatform.Google)
            .ToListAsync(ct);

        return allCompetitorGoogleIdentities.Select(i => new AdvertiserIdentityDto(
            i.Id,
            competitor.Id,
            competitor.Key,
            competitor.Name,
            i.Platform.ToString(),
            i.ExternalId,
            i.Name,
            i.Status.ToString(),
            i.LibraryUrl,
            i.OwnershipEvidenceUrl,
            i.Notes,
            i.ReviewedAt,
            i.CreatedAt)).ToList();
    }
}
