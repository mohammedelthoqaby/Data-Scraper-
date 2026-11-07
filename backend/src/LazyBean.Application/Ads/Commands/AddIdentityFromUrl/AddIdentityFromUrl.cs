using FluentValidation.Results;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Commands.AddIdentityFromUrl;

// Adds a Meta page for a competitor from a Facebook link. A link with a page ID is listed at once, as Pending. A
// link with only a page name is handed to the desktop app, which looks up the ID; the page is listed when it does.
public sealed record AddIdentityFromUrlCommand(Guid CompetitorId, string Url) : IRequest<AddIdentityFromUrlResultDto>;

public sealed record AddIdentityFromUrlResultDto(
    string Status,
    string Message,
    Guid? IdentityId,
    Guid? JobId,
    string? PageId);

public sealed class AddIdentityFromUrlCommandHandler(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ICollectorJobService jobs)
    : IRequestHandler<AddIdentityFromUrlCommand, AddIdentityFromUrlResultDto>
{
    public async Task<AddIdentityFromUrlResultDto> Handle(AddIdentityFromUrlCommand request, CancellationToken ct)
    {
        var competitor = await unitOfWork.Competitors.GetByIdAsync(request.CompetitorId, ct)
            ?? throw new NotFoundException(nameof(Competitor), request.CompetitorId);

        var reference = FacebookPageUrl.Parse(request.Url)
            ?? throw new ValidationException([new ValidationFailure("Url", "Paste a Facebook page link, for example https://www.facebook.com/yourpage.")]);

        if (reference.PageId is { } pageId)
        {
            var existing = await readContext.Query<AdvertiserIdentity>()
                .FirstOrDefaultAsync(i => i.Platform == AdPlatform.Meta && i.ExternalId == pageId, ct);
            if (existing is not null)
            {
                return new AddIdentityFromUrlResultDto(
                    "exists",
                    $"Meta page {pageId} is already listed ({existing.Status}).",
                    existing.Id,
                    null,
                    pageId);
            }

            var identity = new AdvertiserIdentity
            {
                CompetitorId = competitor.Id,
                Platform = AdPlatform.Meta,
                ExternalId = pageId,
                Name = competitor.Name,
                Status = AdvertiserIdentityStatus.Pending,
                LibraryUrl = MetaLibraryUrl(pageId),
                Notes = "Added from a Facebook link; awaiting approval",
            };
            await unitOfWork.AdvertiserIdentities.AddAsync(identity, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return new AddIdentityFromUrlResultDto(
                "pending",
                $"Added Meta page {pageId} for {competitor.Name}. Approve it to start collecting its ads.",
                identity.Id,
                null,
                pageId);
        }

        var slug = reference.Slug!;
        var jobId = await jobs.PlanResolveMetaPageAsync(competitor.Id, slug, ct);
        var connected = await jobs.IsCollectorConnectedAsync(ct);

        return connected
            ? new AddIdentityFromUrlResultDto(
                "resolving",
                $"The LazyBean Collector app will look up {slug} and add its page ID for approval.",
                null,
                jobId,
                null)
            : new AddIdentityFromUrlResultDto(
                "waiting_for_app",
                $"Queued. Open LazyBean Collector on the PC to look up {slug}; nothing is added until it does.",
                null,
                jobId,
                null);
    }

    public static string MetaLibraryUrl(string pageId) =>
        $"https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=EG&view_all_page_id={pageId}";
}
