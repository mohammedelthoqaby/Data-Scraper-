using LazyBean.Domain.Rules;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ads.Queries.GetAdsSummary;

// Reconciles the ads for each competitor and platform. Ingested is the most recent batch of each approved account,
// so it is what the collector app sent last. Stored counts the ad rows. Displayed counts the rows the Ads page shows
// under its default filter (Active, with the Google 30-day rule).
public class GetAdsSummaryQueryHandler(IReadDbContext readContext, AdsOptions options)
    : IRequestHandler<GetAdsSummaryQuery, AdsSummaryDto>
{
    private static readonly TimeSpan BatchHistory = TimeSpan.FromDays(60);

    public async Task<AdsSummaryDto> Handle(GetAdsSummaryQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = AdActivity.CutoffFor(now, options.GoogleActiveWindowDays);

        var competitors = await readContext.Query<Competitor>()
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.IsReference })
            .ToListAsync(ct);

        var identities = await readContext.Query<AdvertiserIdentity>()
            .AsNoTracking()
            .Where(i => i.Status == AdvertiserIdentityStatus.Approved)
            .Select(i => new { i.Id, i.CompetitorId, i.Platform })
            .ToListAsync(ct);

        var stored = await readContext.Query<Ad>()
            .AsNoTracking()
            .Where(a => a.CreativeHash != "blocked")
            .GroupBy(a => new { a.CompetitorId, a.Platform })
            .Select(g => new
            {
                g.Key.CompetitorId,
                g.Key.Platform,
                Stored = g.Count(),
                Displayed = g.Count(a => a.Status == AdStatus.Active && (a.Platform != AdPlatform.Google || a.LastSeenAt >= cutoff)),
            })
            .ToListAsync(ct);

        var batches = await readContext.Query<AdIngestBatch>()
            .AsNoTracking()
            .Where(b => b.ReceivedAt >= now - BatchHistory)
            .Select(b => new { b.IdentityId, b.Received, b.ReceivedAt, b.Status, b.Error })
            .ToListAsync(ct);

        var latestByIdentity = batches
            .GroupBy(b => b.IdentityId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.ReceivedAt).First());

        var rows = new List<AdsSummaryRowDto>();
        foreach (var competitor in competitors.OrderBy(c => c.IsReference).ThenBy(c => c.Name))
        {
            foreach (var platform in new[] { AdPlatform.Meta, AdPlatform.Google })
            {
                var accounts = identities.Where(i => i.CompetitorId == competitor.Id && i.Platform == platform).ToList();
                var latest = accounts
                    .Where(a => latestByIdentity.ContainsKey(a.Id))
                    .Select(a => latestByIdentity[a.Id])
                    .ToList();
                var storedRow = stored.FirstOrDefault(s => s.CompetitorId == competitor.Id && s.Platform == platform);

                if (accounts.Count == 0 && storedRow is null)
                {
                    continue;
                }

                var successful = latest.Where(b => b.Status == "Success").ToList();
                var lastBlocked = latest
                    .Where(b => b.Status == "Blocked")
                    .OrderByDescending(b => b.ReceivedAt)
                    .FirstOrDefault();

                rows.Add(new AdsSummaryRowDto(
                    competitor.Id,
                    competitor.Name,
                    competitor.IsReference,
                    platform.ToString(),
                    accounts.Count,
                    successful.Sum(b => b.Received),
                    storedRow?.Stored ?? 0,
                    storedRow?.Displayed ?? 0,
                    successful.Count == 0 ? null : successful.Max(b => b.ReceivedAt),
                    lastBlocked?.Error));
            }
        }

        return new AdsSummaryDto(rows, now);
    }
}
