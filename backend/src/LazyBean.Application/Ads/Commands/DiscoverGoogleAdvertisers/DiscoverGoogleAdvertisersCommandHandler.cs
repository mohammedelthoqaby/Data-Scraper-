using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Ads.Exceptions;
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
    : IRequestHandler<DiscoverGoogleAdvertisersCommand, GoogleDiscoveryResultDto>
{
    public async Task<GoogleDiscoveryResultDto> Handle(DiscoverGoogleAdvertisersCommand request, CancellationToken ct)
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
        string? blockedReason = null;
        string? errorReason = null;

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
            catch (GoogleAdsBlockedException ex)
            {
                blockedReason = ex.Message;
                logger.LogWarning(ex, "Google Ads Transparency blocked candidate search for competitor '{Name}' with query '{Query}'.", competitor.Name, q);
            }
            catch (Exception ex)
            {
                errorReason = ex.Message;
                logger.LogWarning(ex, "Failed to search Google Ads Transparency candidates for competitor '{Name}' with query '{Query}'.", competitor.Name, q);
            }
        }

        // Idempotently create AdvertiserIdentity in Pending status for each candidate
        var existingIdentities = await readContext.Query<AdvertiserIdentity>()
            .Where(i => i.CompetitorId == competitor.Id && i.Platform == AdPlatform.Google)
            .ToListAsync(ct);
        var existingByExtId = existingIdentities.ToDictionary(i => i.ExternalId, StringComparer.OrdinalIgnoreCase);

        var hasNew = false;

        foreach (var (advId, candidate) in allCandidates)
        {
            if (existingByExtId.ContainsKey(advId))
            {
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
            hasNew = true;
        }

        if (hasNew)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Discovered and saved new Google advertiser identities for competitor '{Name}'.", competitor.Name);
        }

        // Return all identities for this competitor (both existing and newly created)
        var allCompetitorGoogleIdentities = await readContext.Query<AdvertiserIdentity>()
            .Where(i => i.CompetitorId == competitor.Id && i.Platform == AdPlatform.Google)
            .ToListAsync(ct);

        var identityDtos = allCompetitorGoogleIdentities.Select(i => new AdvertiserIdentityDto(
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

        var (status, message) = ResolveOutcome(competitor.Name, allCandidates.Count, blockedReason, errorReason);
        return new GoogleDiscoveryResultDto(status, allCandidates.Count, message, identityDtos);
    }

    private static (string Status, string? Message) ResolveOutcome(
        string competitorName, int candidatesFound, string? blockedReason, string? errorReason)
    {
        if (candidatesFound > 0)
        {
            return ("found", null);
        }

        if (blockedReason is not null)
        {
            return ("blocked", $"Google Ads Transparency blocked the search: {blockedReason}");
        }

        if (errorReason is not null)
        {
            return ("error", $"Google Ads discovery failed for {competitorName}: {errorReason}");
        }

        return ("none", $"No Google advertiser found for {competitorName}.");
    }
}
