using LazyBean.Application.Ads.Common;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ads;

// Brings existing ads up to the current link and price rules, and links any ad that has no campaign. It runs in the
// background at startup, in batches, and only touches ads below AdDerivedFields.CurrentVersion, so a later start
// does no work.
public sealed class AdsBackfillService(
    IDbContextFactory<LazyBeanDbContext> contextFactory,
    ILogger<AdsBackfillService> logger) : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let the API finish starting before the first batch.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            var updated = await RunAsync(stoppingToken);
            if (updated > 0)
            {
                logger.LogInformation("Ads backfill: recomputed the price, quantity and product link of {Count} ad(s).", updated);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: the next start continues where this one stopped.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ads backfill stopped; it will run again on the next start.");
        }
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await using var db = await contextFactory.CreateDbContextAsync(ct);

            var pending = await db.Ads
                .Where(a => a.LinkVersion < AdDerivedFields.CurrentVersion || a.CampaignId == null)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (pending.Count == 0)
            {
                return total;
            }

            var competitorIds = pending.Select(a => a.CompetitorId).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => competitorIds.Contains(p.CompetitorId))
                .Select(p => new { p.Id, p.CompetitorId, p.Title, p.Handle, p.CanonicalUrl })
                .ToListAsync(ct);
            var catalogs = products
                .GroupBy(p => p.CompetitorId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<LinkableProduct>)g.Select(p => new LinkableProduct(p.Id, p.Title, p.Handle, p.CanonicalUrl)).ToList());

            var identityIds = pending.Select(a => a.IdentityId).Distinct().ToList();
            var campaigns = await db.AdCampaigns.AsNoTracking()
                .Where(c => identityIds.Contains(c.IdentityId))
                .Select(c => new { c.Id, c.IdentityId, c.CreativeHash, c.Platform, c.CreatedAt })
                .ToListAsync(ct);
            var campaignByIdentityHash = new Dictionary<(Guid, string), Guid>();
            foreach (var campaign in campaigns.OrderBy(c => c.CreatedAt))
            {
                campaignByIdentityHash.TryAdd((campaign.IdentityId, campaign.CreativeHash), campaign.Id);
            }

            foreach (var ad in pending)
            {
                if (ad.CampaignId is null && campaignByIdentityHash.TryGetValue((ad.IdentityId, ad.CreativeHash), out var campaignId))
                {
                    ad.CampaignId = campaignId;
                }

                var catalog = catalogs.TryGetValue(ad.CompetitorId, out var found) ? found : Array.Empty<LinkableProduct>();
                AdDerivedFields.Apply(ad, catalog);
            }

            await db.SaveChangesAsync(ct);
            total += pending.Count;
        }
    }
}
