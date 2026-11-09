using LazyBean.Application.Ads.Comparison;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LazyBean.Application.Alerts.Dtos;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Alerts;

public class AlertService : IAlertService
{
    private readonly LazyBeanDbContext _dbContext;
    private readonly IAiClient _aiClient;
    private readonly IMetaAdLibraryClient _metaAdClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        LazyBeanDbContext dbContext,
        IAiClient aiClient,
        IMetaAdLibraryClient metaAdClient,
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        IConfiguration config,
        ILogger<AlertService> logger)
    {
        _dbContext = dbContext;
        _aiClient = aiClient;
        _metaAdClient = metaAdClient;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task<AlertSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var setting = await _dbContext.AlertSettings.FirstOrDefaultAsync(ct);
        if (setting == null)
        {
            setting = new AlertSetting();
            _dbContext.AlertSettings.Add(setting);
            await _dbContext.SaveChangesAsync(ct);
        }

        return new AlertSettingsDto(
            setting.AdMoreExpensiveGapPercent,
            setting.PriceDropPercent,
            setting.LazyBeanAboveCheapestPercent,
            setting.OutOfStockCompetitorCount,
            setting.MarginRoomBelowCheapestPercent,
            setting.UncarriedBrandCompetitorCount,
            setting.BigCampaignDiscountCount,
            setting.ProlongedGapDays,
            setting.MaterialChangePercentPoints);
    }

    public async Task<AlertSettingsDto> UpdateSettingsAsync(AlertSettingsDto dto, CancellationToken ct = default)
    {
        var setting = await _dbContext.AlertSettings.FirstOrDefaultAsync(ct);
        if (setting == null)
        {
            setting = new AlertSetting();
            _dbContext.AlertSettings.Add(setting);
        }

        setting.AdMoreExpensiveGapPercent = dto.AdMoreExpensiveGapPercent;
        setting.PriceDropPercent = dto.PriceDropPercent;
        setting.LazyBeanAboveCheapestPercent = dto.LazyBeanAboveCheapestPercent;
        setting.OutOfStockCompetitorCount = dto.OutOfStockCompetitorCount;
        setting.MarginRoomBelowCheapestPercent = dto.MarginRoomBelowCheapestPercent;
        setting.UncarriedBrandCompetitorCount = dto.UncarriedBrandCompetitorCount;
        setting.BigCampaignDiscountCount = dto.BigCampaignDiscountCount;
        setting.ProlongedGapDays = dto.ProlongedGapDays;
        setting.MaterialChangePercentPoints = dto.MaterialChangePercentPoints;
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return dto;
    }

    public async Task<DataHealthDto> GetDataHealthAsync(CancellationToken ct = default)
    {
        var cutoff48h = DateTimeOffset.UtcNow.AddHours(-48);

        // 1. Failed/partial runs in last 48h
        var failedRuns = await _dbContext.CollectionRuns
            .Where(r => r.StartedAt >= cutoff48h && (r.Status == CollectionRunStatus.Failed || r.Status == CollectionRunStatus.Partial))
            .CountAsync(ct);

        // 2. Stale competitors (> 48h since latest observation)
        var competitors = await _dbContext.Competitors
            .Where(c => !c.IsReference)
            .ToListAsync(ct);

        var staleCompetitors = new List<string>();
        foreach (var c in competitors)
        {
            var latestObs = await (
                from po in _dbContext.PriceObservations
                join v in _dbContext.Variants on po.VariantId equals v.Id
                join p in _dbContext.Products on v.ProductId equals p.Id
                where p.CompetitorId == c.Id
                orderby po.ObservedAt descending
                select (DateTimeOffset?)po.ObservedAt)
                .FirstOrDefaultAsync(ct);

            if (latestObs == null || latestObs < cutoff48h)
            {
                staleCompetitors.Add(c.Name);
            }
        }

        // 3. Blocked ad collectors
        var blockedCollectors = await _dbContext.AdvertiserIdentities
            .Where(i => i.Status == AdvertiserIdentityStatus.Blocked || (i.BlockedUntil.HasValue && i.BlockedUntil.Value > DateTimeOffset.UtcNow))
            .Select(i => i.Name)
            .ToListAsync(ct);

        // 4. Meta token status
        var metaDebug = await _metaAdClient.DebugTokenAsync(ct);
        var tokenStatus = metaDebug.IsValid ? "Active & Valid" : $"Inactive: {metaDebug.ErrorMessage ?? "Token expired or missing"}";

        var allHealthy = failedRuns == 0 && staleCompetitors.Count == 0 && blockedCollectors.Count == 0 && metaDebug.IsValid;

        return new DataHealthDto(
            allHealthy,
            failedRuns,
            staleCompetitors,
            blockedCollectors,
            tokenStatus,
            metaDebug.ExpiresAt);
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        AlertSeverity? severity = null,
        AlertStatus? status = null,
        bool? isBestseller = null,
        string? search = null,
        CancellationToken ct = default)
    {
        // First, ensure alerts exist / sync if empty
        var anyAlerts = await _dbContext.Alerts.AnyAsync(ct);
        if (!anyAlerts)
        {
            await SyncAlertsAsync(ct);
        }

        var query = _dbContext.Alerts.AsNoTracking().AsQueryable();

        if (severity.HasValue)
        {
            query = query.Where(a => a.Severity == severity.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }
        else
        {
            // Default to Active
            query = query.Where(a => a.Status == AlertStatus.Active);
        }

        if (isBestseller.HasValue)
        {
            query = query.Where(a => a.IsBestseller == isBestseller.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(a =>
                a.Title.ToLower().Contains(s) ||
                a.Reason.ToLower().Contains(s) ||
                (a.ReferenceProductTitle != null && a.ReferenceProductTitle.ToLower().Contains(s)) ||
                (a.CompetitorProductTitle != null && a.CompetitorProductTitle.ToLower().Contains(s)) ||
                (a.CompetitorName != null && a.CompetitorName.ToLower().Contains(s)));
        }

        var list = await query
            .OrderBy(a => a.Severity)
            .ThenByDescending(a => a.IsBestseller)
            .ThenByDescending(a => a.EgpImpact)
            .ThenByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<AlertSummaryDto> GetAlertSummaryAsync(CancellationToken ct = default)
    {
        var activeQuery = _dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.Status == AlertStatus.Active);

        var total = await activeQuery.CountAsync(ct);
        var urgent = await activeQuery.CountAsync(a => a.Severity == AlertSeverity.Urgent, ct);
        var opp = await activeQuery.CountAsync(a => a.Severity == AlertSeverity.Opportunity, ct);
        var watch = await activeQuery.CountAsync(a => a.Severity == AlertSeverity.Watch, ct);

        var byTypeGroup = await activeQuery
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        var byType = byTypeGroup.ToDictionary(x => x.Type, x => x.Count);

        return new AlertSummaryDto(total, urgent, opp, watch, byType);
    }

    public async Task<AlertDto> PerformActionAsync(Guid alertId, string action, string? note, string user, CancellationToken ct = default)
    {
        var alert = await _dbContext.Alerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert == null)
            throw new KeyNotFoundException($"Alert {alertId} not found");

        var act = action.Trim().ToLowerInvariant();
        var historyAction = "Updated";

        if (act == "done")
        {
            alert.Status = AlertStatus.Done;
            alert.ResolvedAt = DateTimeOffset.UtcNow;
            historyAction = "Done";
        }
        else if (act == "snooze" || act == "snooze7d")
        {
            alert.Status = AlertStatus.Snoozed;
            alert.SnoozedUntil = DateTimeOffset.UtcNow.AddDays(7);
            historyAction = "Snoozed";
        }
        else if (act == "dismiss")
        {
            alert.Status = AlertStatus.Dismissed;
            alert.DismissNote = note;
            historyAction = "Dismissed";
        }
        else if (act == "reopen" || act == "active")
        {
            alert.Status = AlertStatus.Active;
            alert.SnoozedUntil = null;
            historyAction = "Reopened";
        }

        alert.UpdatedAt = DateTimeOffset.UtcNow;

        var history = new AlertHistory
        {
            AlertId = alert.Id,
            Action = historyAction,
            Note = note,
            PerformedBy = string.IsNullOrWhiteSpace(user) ? "manager" : user,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.AlertHistories.Add(history);

        await _dbContext.SaveChangesAsync(ct);
        return ToDto(alert);
    }

    public async Task<IReadOnlyList<AlertHistoryDto>> GetHistoryAsync(Guid alertId, CancellationToken ct = default)
    {
        var items = await _dbContext.AlertHistories
            .AsNoTracking()
            .Where(h => h.AlertId == alertId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);

        return items.Select(h => new AlertHistoryDto(h.Id, h.AlertId, h.Action, h.Note, h.PerformedBy, h.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<AlertDto>> SyncAlertsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Alert Computation & Sync...");
        var settings = await GetSettingsAsync(ct);

        // Fetch bestsellers set
        var bestsellers = await GetBestsellerExternalIdsAsync(ct);

        // Compute candidates
        var candidates = new List<Alert>();

        // 1. Matched Products Data
        var matches = await (
            from m in _dbContext.ProductMatches
            where m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant
            join cv in _dbContext.Variants on m.CompetitorVariantId equals cv.Id
            join cp in _dbContext.Products on cv.ProductId equals cp.Id
            join c in _dbContext.Competitors on cp.CompetitorId equals c.Id
            join rv in _dbContext.Variants on m.ReferenceVariantId equals rv.Id
            join rp in _dbContext.Products on rv.ProductId equals rp.Id
            where !c.IsReference && cp.Status == ProductStatus.Active && rp.Status == ProductStatus.Active
            select new
            {
                MatchId = m.Id,
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                CompetitorProductId = cp.Id,
                CompetitorProductTitle = cp.Title,
                CompetitorProductUrl = cp.CanonicalUrl,
                CompetitorVariantId = cv.Id,
                ReferenceProductId = rp.Id,
                ReferenceExternalId = rp.ExternalId,
                ReferenceProductTitle = rp.Title,
                ReferenceProductUrl = rp.CanonicalUrl,
                ReferenceVariantId = rv.Id,
                ReferenceBrand = rp.Brand ?? rp.Vendor
            }).ToListAsync(ct);

        var refVarIds = matches.Select(m => m.ReferenceVariantId).Distinct().ToList();
        var compVarIds = matches.Select(m => m.CompetitorVariantId).Distinct().ToList();

        var observations = await _dbContext.PriceObservations
            .Where(po => refVarIds.Contains(po.VariantId) || compVarIds.Contains(po.VariantId))
            .OrderByDescending(po => po.ObservedAt)
            .ToListAsync(ct);

        var latestObsByVar = observations
            .GroupBy(po => po.VariantId)
            .ToDictionary(g => g.Key, g => g.First());

        var obsHistoryByVar = observations
            .GroupBy(po => po.VariantId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ObservedAt).ToList());

        // Group matches by Reference Variant to calculate cheapest and competitor count
        var groupedByRef = matches.GroupBy(m => m.ReferenceVariantId).ToList();

        // Query active Ads
        // Our own (reference) ads never count as competitor ads: they are excluded from these alerts.
        var referenceCompetitorIds = await _dbContext.Competitors
            .Where(c => c.IsReference)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var activeAds = await _dbContext.Ads
            .Where(a => a.Status == AdStatus.Active && !referenceCompetitorIds.Contains(a.CompetitorId))
            .ToListAsync(ct);

        // Query active Offers
        var recentOffers = await _dbContext.Offers
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var refGroup in groupedByRef)
        {
            var first = refGroup.First();
            if (!latestObsByVar.TryGetValue(first.ReferenceVariantId, out var refObs) || refObs.Price <= 0)
                continue;

            var refPrice = Math.Round(refObs.Price, 2);
            var refInStock = refObs.InStock ?? true;
            var isBestseller = bestsellers.Contains(first.ReferenceExternalId) ||
                               bestsellers.Any(b => first.ReferenceProductTitle.Contains(b, StringComparison.OrdinalIgnoreCase));

            var compItems = new List<(string CompName, Guid CompProdId, string CompTitle, string CompUrl, Guid CompVarId, Guid CompId, decimal Price, bool InStock, decimal? CompareAt)>();

            foreach (var m in refGroup)
            {
                if (latestObsByVar.TryGetValue(m.CompetitorVariantId, out var cObs) && cObs.Price > 0)
                {
                    compItems.Add((
                        m.CompetitorName,
                        m.CompetitorProductId,
                        m.CompetitorProductTitle,
                        m.CompetitorProductUrl,
                        m.CompetitorVariantId,
                        m.CompetitorId,
                        Math.Round(cObs.Price, 2),
                        cObs.InStock ?? true,
                        cObs.CompareAtPrice));
                }
            }

            if (compItems.Count == 0) continue;

            var inStockComps = compItems.Where(c => c.InStock && c.Price > 0).OrderBy(c => c.Price).ToList();
            var cheapestInStock = inStockComps.FirstOrDefault();

            // -------------------------------------------------------------
            // Urgent Alert a: Competitor ad on a product where Lazy Bean is >= 10% more expensive
            // -------------------------------------------------------------
            foreach (var comp in compItems)
            {
                if (refPrice > comp.Price)
                {
                    var gapPct = Math.Round(((refPrice - comp.Price) / comp.Price) * 100m, 1);
                    if (gapPct >= settings.AdMoreExpensiveGapPercent)
                    {
                        var matchingAds = activeAds.Where(a =>
                            a.CompetitorId == comp.CompId &&
                            (a.ProductId == comp.CompProdId ||
                             (!string.IsNullOrEmpty(a.Body) && a.Body.Contains(first.ReferenceBrand ?? "coffee", StringComparison.OrdinalIgnoreCase)))).ToList();

                        if (matchingAds.Count > 0)
                        {
                            var diff = Math.Round(refPrice - comp.Price, 2);
                            var ad = matchingAds.First();
                            var key = $"ad_gap_{first.ReferenceProductId}_{comp.CompId}";

                            candidates.Add(new Alert
                            {
                                Type = AlertType.CompetitorAdMoreExpensive,
                                Severity = AlertSeverity.Urgent,
                                Title = $"{comp.CompName} advertising active discount on {first.ReferenceProductTitle}",
                                Reason = $"Lazy Bean price is {refPrice:F2} EGP (+{gapPct:F1}% vs {comp.CompName} {comp.Price:F2} EGP, gap of {diff:F2} EGP) while competitor is running {matchingAds.Count} active ad campaign(s).",
                                TitleAr = $"حملة إعلانية نشطة من {comp.CompName} على {first.ReferenceProductTitle}",
                                ReasonAr = $"سعر ليزي بين الحالي {refPrice:F2} ج.م أعلى بنسبة {gapPct:F1}% مقارنة بسعر {comp.CompName} ({comp.Price:F2} ج.م بفارق {diff:F2} ج.م) بالتزامن مع إطلاق {matchingAds.Count} إعلانات ترويجية نشطة.",
                                SuggestedAction = $"Match competitor price to {comp.Price:F2} EGP or launch counter-ad campaign to protect market share.",
                                SuggestedActionAr = $"مطابقة السعر مع {comp.CompName} عند {comp.Price:F2} ج.م أو إطلاق حملة ترويجية مضادة لحماية الحصة السوقية.",
                                ReferenceProductId = first.ReferenceProductId,
                                ReferenceProductTitle = first.ReferenceProductTitle,
                                ReferenceProductUrl = first.ReferenceProductUrl,
                                CompetitorProductId = comp.CompProdId,
                                CompetitorProductTitle = comp.CompTitle,
                                CompetitorProductUrl = comp.CompUrl,
                                CompetitorName = comp.CompName,
                                AdLibraryUrl = ad.SnapshotUrl ?? ad.LinkUrl ?? "https://www.facebook.com/ads/library",
                                IsBestseller = isBestseller,
                                EgpImpact = diff,
                                MetricValuesJson = JsonSerializer.Serialize(new { gapPercent = gapPct, lazyBeanPrice = refPrice, competitorPrice = comp.Price, adCount = matchingAds.Count }),
                                IdentityKey = key
                            });
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // Urgent Alert b: Competitor price drop >= 10% this week, now cheaper than Lazy Bean
            // -------------------------------------------------------------
            foreach (var comp in compItems)
            {
                if (comp.Price < refPrice && obsHistoryByVar.TryGetValue(comp.CompVarId, out var compObsList) && compObsList.Count > 1)
                {
                    var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
                    var olderObs = compObsList.Where(o => o.ObservedAt <= weekAgo).OrderByDescending(o => o.ObservedAt).FirstOrDefault();
                    var baselinePrice = olderObs?.Price ?? comp.CompareAt ?? 0m;

                    if (baselinePrice > comp.Price)
                    {
                        var dropPct = Math.Round(((baselinePrice - comp.Price) / baselinePrice) * 100m, 1);
                        if (dropPct >= settings.PriceDropPercent)
                        {
                            var diff = Math.Round(refPrice - comp.Price, 2);
                            var key = $"price_drop_{first.ReferenceProductId}_{comp.CompId}";

                            candidates.Add(new Alert
                            {
                                Type = AlertType.PriceDropCompetitorCheaper,
                                Severity = AlertSeverity.Urgent,
                                Title = $"{comp.CompName} dropped price by {dropPct:F1}% on {first.ReferenceProductTitle}",
                                Reason = $"{comp.CompName} slashed price from {baselinePrice:F2} EGP to {comp.Price:F2} EGP (-{dropPct:F1}%), undercutting Lazy Bean's {refPrice:F2} EGP by {diff:F2} EGP.",
                                TitleAr = $"تخفيض سعر بنسبة {dropPct:F1}% من {comp.CompName} على {first.ReferenceProductTitle}",
                                ReasonAr = $"قام {comp.CompName} بخفض السعر من {baselinePrice:F2} ج.م إلى {comp.Price:F2} ج.م (-{dropPct:F1}%)، ليصبح أقل من سعر ليزي بين ({refPrice:F2} ج.م) بفارق {diff:F2} ج.م.",
                                SuggestedAction = $"Evaluate price match down to {comp.Price:F2} EGP or bundle with complementary roast beans.",
                                SuggestedActionAr = $"دراسة مطابقة السعر حتى {comp.Price:F2} ج.م أو عمل باقة مدمجة مع حبوب البن المكملة.",
                                ReferenceProductId = first.ReferenceProductId,
                                ReferenceProductTitle = first.ReferenceProductTitle,
                                ReferenceProductUrl = first.ReferenceProductUrl,
                                CompetitorProductId = comp.CompProdId,
                                CompetitorProductTitle = comp.CompTitle,
                                CompetitorProductUrl = comp.CompUrl,
                                CompetitorName = comp.CompName,
                                IsBestseller = isBestseller,
                                EgpImpact = diff,
                                MetricValuesJson = JsonSerializer.Serialize(new { dropPercent = dropPct, baselinePrice, competitorPrice = comp.Price, lazyBeanPrice = refPrice }),
                                IdentityKey = key
                            });
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // Urgent Alert c: Lazy Bean >= 15% above the cheapest in-stock competitor
            // -------------------------------------------------------------
            if (refInStock && cheapestInStock != default)
            {
                var gapPct = Math.Round(((refPrice - cheapestInStock.Price) / cheapestInStock.Price) * 100m, 1);
                if (gapPct >= settings.LazyBeanAboveCheapestPercent)
                {
                    var diff = Math.Round(refPrice - cheapestInStock.Price, 2);
                    var key = $"above_cheapest_{first.ReferenceProductId}_{cheapestInStock.CompId}";

                    candidates.Add(new Alert
                    {
                        Type = AlertType.LazyBeanMoreExpensiveInStock,
                        Severity = AlertSeverity.Urgent,
                        Title = $"Lazy Bean is {gapPct:F1}% above cheapest in-stock seller on {first.ReferenceProductTitle}",
                        Reason = $"Current Lazy Bean price is {refPrice:F2} EGP while {cheapestInStock.CompName} offers it available in-stock for {cheapestInStock.Price:F2} EGP (unfavorable gap of {diff:F2} EGP).",
                        TitleAr = $"سعر ليزي بين أعلى بنسبة {gapPct:F1}% عن أرخص منافس متوفر لـ {first.ReferenceProductTitle}",
                        ReasonAr = $"سعر ليزي بين الحالي {refPrice:F2} ج.م بينما يوفره {cheapestInStock.CompName} متوفراً في المخزون بسعر {cheapestInStock.Price:F2} ج.م (فارق غير منافس قدره {diff:F2} ج.م).",
                        SuggestedAction = $"Adjust price toward {cheapestInStock.Price:F2} EGP to recapture high-intent shoppers.",
                        SuggestedActionAr = $"تعديل السعر نحو {cheapestInStock.Price:F2} ج.م لاستعادة العملاء الراغبين في الشراء الفوري.",
                        ReferenceProductId = first.ReferenceProductId,
                        ReferenceProductTitle = first.ReferenceProductTitle,
                        ReferenceProductUrl = first.ReferenceProductUrl,
                        CompetitorProductId = cheapestInStock.CompProdId,
                        CompetitorProductTitle = cheapestInStock.CompTitle,
                        CompetitorProductUrl = cheapestInStock.CompUrl,
                        CompetitorName = cheapestInStock.CompName,
                        IsBestseller = isBestseller,
                        EgpImpact = diff,
                        MetricValuesJson = JsonSerializer.Serialize(new { gapPercent = gapPct, lazyBeanPrice = refPrice, competitorPrice = cheapestInStock.Price }),
                        IdentityKey = key
                    });
                }
            }

            // -------------------------------------------------------------
            // Urgent Alert d: Lazy Bean out of stock while >= 2 competitors have it in stock
            // -------------------------------------------------------------
            if (!refInStock && inStockComps.Count >= settings.OutOfStockCompetitorCount)
            {
                var compNames = string.Join(", ", inStockComps.Select(c => c.CompName).Distinct());
                var avgCompPrice = Math.Round(inStockComps.Average(c => c.Price), 2);
                var key = $"stock_multi_comp_{first.ReferenceProductId}";

                candidates.Add(new Alert
                {
                    Type = AlertType.LazyBeanOutOfStockMultiCompetitor,
                    Severity = AlertSeverity.Urgent,
                    Title = $"Lazy Bean out of stock while {inStockComps.Count} competitors have stock for {first.ReferenceProductTitle}",
                    Reason = $"Product is out of stock at Lazy Bean while {inStockComps.Count} competitors ({compNames}) are fulfilling orders at average price of {avgCompPrice:F2} EGP.",
                    TitleAr = $"نفاد المخزون لدى ليزي بين مع توفره لدى {inStockComps.Count} منافسين لـ {first.ReferenceProductTitle}",
                    ReasonAr = $"المنتج غير متوفر حالياً لدى ليزي بين بينما يقوم {inStockComps.Count} منافسين ({compNames}) بتلبية الطلبات بمتوسط سعر {avgCompPrice:F2} ج.م.",
                    SuggestedAction = $"Expedite supplier restocking: lost revenue opportunity estimated at {refPrice:F2} EGP per unit.",
                    SuggestedActionAr = $"التعجيل بإعادة طلب التوريد للمخزون: فقدان إيرادات يقدر بـ {refPrice:F2} ج.م لكل وحدة.",
                    ReferenceProductId = first.ReferenceProductId,
                    ReferenceProductTitle = first.ReferenceProductTitle,
                    ReferenceProductUrl = first.ReferenceProductUrl,
                    CompetitorProductId = inStockComps[0].CompProdId,
                    CompetitorProductTitle = inStockComps[0].CompTitle,
                    CompetitorProductUrl = inStockComps[0].CompUrl,
                    CompetitorName = inStockComps[0].CompName,
                    IsBestseller = isBestseller,
                    EgpImpact = refPrice,
                    MetricValuesJson = JsonSerializer.Serialize(new { inStockCompetitorCount = inStockComps.Count, averageCompetitorPrice = avgCompPrice, lazyBeanPrice = refPrice }),
                    IdentityKey = key
                });
            }

            // -------------------------------------------------------------
            // Opportunity Alert e: All competitors out of stock while Lazy Bean is in stock
            // -------------------------------------------------------------
            if (refInStock && inStockComps.Count == 0 && compItems.Count > 0)
            {
                var totalComps = compItems.Count;
                var key = $"opp_exclusive_stock_{first.ReferenceProductId}";

                candidates.Add(new Alert
                {
                    Type = AlertType.AllCompetitorsOutOfStock,
                    Severity = AlertSeverity.Opportunity,
                    Title = $"Exclusive inventory: All {totalComps} competitor stores are out of stock on {first.ReferenceProductTitle}",
                    Reason = $"Lazy Bean is the only store with confirmed stock ({refPrice:F2} EGP) across all {totalComps} tracked market competitors.",
                    TitleAr = $"ميزة مخزون حصرية: جميع المنافسين ({totalComps}) نفد مخزونهم لـ {first.ReferenceProductTitle}",
                    ReasonAr = $"ليزي بين هو المتجر الوحيد المتبقي بمخزون مؤكد ({refPrice:F2} ج.م) بين جميع المنافسين الـ {totalComps} المسجلين.",
                    SuggestedAction = $"Promote product on storefront hero banner or raise price by +5-10% without losing market positioning.",
                    SuggestedActionAr = $"إبراز المنتج في الواجهة الرئيسية للمتجر أو رفع السعر بنسبة +5-10% للاستفادة من انفراد المخزون.",
                    ReferenceProductId = first.ReferenceProductId,
                    ReferenceProductTitle = first.ReferenceProductTitle,
                    ReferenceProductUrl = first.ReferenceProductUrl,
                    CompetitorProductId = compItems[0].CompProdId,
                    CompetitorProductTitle = compItems[0].CompTitle,
                    CompetitorProductUrl = compItems[0].CompUrl,
                    CompetitorName = compItems[0].CompName,
                    IsBestseller = isBestseller,
                    EgpImpact = Math.Round(refPrice * 0.10m, 2),
                    MetricValuesJson = JsonSerializer.Serialize(new { outOfStockCount = totalComps, lazyBeanPrice = refPrice }),
                    IdentityKey = key
                });
            }

            // -------------------------------------------------------------
            // Opportunity Alert f: Lazy Bean >= 15% below cheapest competitor (margin room)
            // -------------------------------------------------------------
            if (refInStock && cheapestInStock != default && refPrice < cheapestInStock.Price)
            {
                var underPct = Math.Round(((cheapestInStock.Price - refPrice) / cheapestInStock.Price) * 100m, 1);
                if (underPct >= settings.MarginRoomBelowCheapestPercent)
                {
                    var diff = Math.Round(cheapestInStock.Price - refPrice, 2);
                    var key = $"opp_margin_room_{first.ReferenceProductId}_{cheapestInStock.CompId}";

                    candidates.Add(new Alert
                    {
                        Type = AlertType.LazyBeanCheaperMarginRoom,
                        Severity = AlertSeverity.Opportunity,
                        Title = $"Margin room: Lazy Bean is {underPct:F1}% below cheapest competitor on {first.ReferenceProductTitle}",
                        Reason = $"Lazy Bean price is {refPrice:F2} EGP compared to cheapest competitor {cheapestInStock.CompName} at {cheapestInStock.Price:F2} EGP (discount gap of {diff:F2} EGP).",
                        TitleAr = $"فرصة لزيادة الهامش: ليزي بين أقل بنسبة {underPct:F1}% عن أرخص منافس لـ {first.ReferenceProductTitle}",
                        ReasonAr = $"سعر ليزي بين {refPrice:F2} ج.م مقارنة بأرخص منافس ({cheapestInStock.CompName}) بسعر {cheapestInStock.Price:F2} ج.م (فارق قدره {diff:F2} ج.م).",
                        SuggestedAction = $"Increase price by {Math.Round(diff * 0.5m, 2):F2} EGP to improve gross margins while remaining the lowest-price destination.",
                        SuggestedActionAr = $"رفع السعر بمقدار {Math.Round(diff * 0.5m, 2):F2} ج.م لتعظيم هامش الربح مع الحفاظ على صدارة السعر الأقل.",
                        ReferenceProductId = first.ReferenceProductId,
                        ReferenceProductTitle = first.ReferenceProductTitle,
                        ReferenceProductUrl = first.ReferenceProductUrl,
                        CompetitorProductId = cheapestInStock.CompProdId,
                        CompetitorProductTitle = cheapestInStock.CompTitle,
                        CompetitorProductUrl = cheapestInStock.CompUrl,
                        CompetitorName = cheapestInStock.CompName,
                        IsBestseller = isBestseller,
                        EgpImpact = diff,
                        MetricValuesJson = JsonSerializer.Serialize(new { marginRoomPercent = underPct, lazyBeanPrice = refPrice, competitorPrice = cheapestInStock.Price }),
                        IdentityKey = key
                    });
                }
            }

            // -------------------------------------------------------------
            // Watch Alert i: Lazy Bean more expensive on the same product for >= 14 days
            // -------------------------------------------------------------
            foreach (var comp in compItems)
            {
                if (refPrice > comp.Price && obsHistoryByVar.TryGetValue(first.ReferenceVariantId, out var refObsList) && refObsList.Count > 1)
                {
                    var earliestGap = refObsList.OrderBy(o => o.ObservedAt).FirstOrDefault();
                    if (earliestGap != null)
                    {
                        var gapDays = (DateTimeOffset.UtcNow - earliestGap.ObservedAt).TotalDays;
                        if (gapDays >= settings.ProlongedGapDays)
                        {
                            var gapPct = Math.Round(((refPrice - comp.Price) / comp.Price) * 100m, 1);
                            var diff = Math.Round(refPrice - comp.Price, 2);
                            var key = $"watch_prolonged_{first.ReferenceProductId}_{comp.CompId}";

                            candidates.Add(new Alert
                            {
                                Type = AlertType.ProlongedPriceGap,
                                Severity = AlertSeverity.Watch,
                                Title = $"Chronic price disadvantage ({Math.Round(gapDays, 0)} days) on {first.ReferenceProductTitle}",
                                Reason = $"Lazy Bean has remained higher than {comp.CompName} ({refPrice:F2} EGP vs {comp.Price:F2} EGP, gap of {diff:F2} EGP) continuously for {Math.Round(gapDays, 0)} days.",
                                TitleAr = $"فجوة سعرية ممتدة ({Math.Round(gapDays, 0)} يوماً) على {first.ReferenceProductTitle}",
                                ReasonAr = $"ظل سعر ليزي بين أعلى من {comp.CompName} ({refPrice:F2} ج.م مقابل {comp.Price:F2} ج.م بفارق {diff:F2} ج.م) بشكل مستمر منذ {Math.Round(gapDays, 0)} يوماً.",
                                SuggestedAction = $"Renegotiate vendor supply terms or review baseline catalog pricing strategy.",
                                SuggestedActionAr = $"إعادة التفاوض على شروط التوريد أو مراجعة استراتيجية التسعير الأساسية للمنتج.",
                                ReferenceProductId = first.ReferenceProductId,
                                ReferenceProductTitle = first.ReferenceProductTitle,
                                ReferenceProductUrl = first.ReferenceProductUrl,
                                CompetitorProductId = comp.CompProdId,
                                CompetitorProductTitle = comp.CompTitle,
                                CompetitorProductUrl = comp.CompUrl,
                                CompetitorName = comp.CompName,
                                IsBestseller = isBestseller,
                                EgpImpact = diff,
                                MetricValuesJson = JsonSerializer.Serialize(new { durationDays = Math.Round(gapDays, 0), lazyBeanPrice = refPrice, competitorPrice = comp.Price }),
                                IdentityKey = key
                            });
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // Opportunity Alert g: Brands sold by >= 2 competitors that Lazy Bean doesn't carry
        // -------------------------------------------------------------
        var allProducts = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && !string.IsNullOrEmpty(p.Brand))
            .Select(p => new { p.CompetitorId, Brand = p.Brand!.Trim() })
            .ToListAsync(ct);

        var lbCompetitorId = await _dbContext.Competitors
            .Where(c => c.IsReference)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        var lbBrands = allProducts
            .Where(p => p.CompetitorId == lbCompetitorId)
            .Select(p => p.Brand.ToLowerInvariant())
            .Distinct()
            .ToHashSet();

        var competitorBrands = allProducts
            .Where(p => p.CompetitorId != lbCompetitorId)
            .GroupBy(p => p.Brand)
            .Select(g => new
            {
                Brand = g.Key,
                CompetitorCount = g.Select(x => x.CompetitorId).Distinct().Count(),
                ProductCount = g.Count()
            })
            .Where(b => b.CompetitorCount >= settings.UncarriedBrandCompetitorCount && !lbBrands.Contains(b.Brand.ToLowerInvariant()))
            .OrderByDescending(b => b.CompetitorCount)
            .ThenByDescending(b => b.ProductCount)
            .Take(5)
            .ToList();

        foreach (var brand in competitorBrands)
        {
            var key = $"opp_uncarried_brand_{brand.Brand.ToLowerInvariant()}";
            candidates.Add(new Alert
            {
                Type = AlertType.UncarriedCompetitorBrands,
                Severity = AlertSeverity.Opportunity,
                Title = $"High competitor traction for uncarried brand: {brand.Brand}",
                Reason = $"{brand.Brand} is actively carried by {brand.CompetitorCount} rival stores with {brand.ProductCount} catalog products, but is absent from Lazy Bean.",
                TitleAr = $"طلب منافسين مرتفع على علامة غير متوفرة: {brand.Brand}",
                ReasonAr = $"يتم تداول علامة {brand.Brand} بنشاط لدى {brand.CompetitorCount} متاجر منافسة بإجمالي {brand.ProductCount} منتجاً دون توفرها لدى ليزي بين.",
                SuggestedAction = $"Contact distributor to evaluate sourcing {brand.Brand} line for Lazy Bean customers.",
                SuggestedActionAr = $"التواصل مع الموزع المعتمد لبحث توفير خط منتجات {brand.Brand} لعملاء ليزي بين.",
                IsBestseller = false,
                EgpImpact = 500m,
                MetricValuesJson = JsonSerializer.Serialize(new { competitorCount = brand.CompetitorCount, productCount = brand.ProductCount }),
                IdentityKey = key
            });
        }

        // -------------------------------------------------------------
        // Watch Alert h: Big competitor campaign (ad discount % or >= 10 new discounts this week)
        // -------------------------------------------------------------
        var promoAds = activeAds.Where(a =>
            !string.IsNullOrEmpty(a.Body) &&
            Regex.IsMatch(a.Body, @"(\d+%\s*(off|خصم))|(خصم\s*\d+%)|(sale)|(تخفيض)", RegexOptions.IgnoreCase))
            .ToList();

        foreach (var ad in promoAds.Take(5))
        {
            var comp = await _dbContext.Competitors.FirstOrDefaultAsync(c => c.Id == ad.CompetitorId, ct);
            var compName = comp?.Name ?? "Competitor";
            var matchDiscount = Regex.Match(ad.Body ?? "", @"\d+%", RegexOptions.IgnoreCase);
            var discountStr = matchDiscount.Success ? matchDiscount.Value : "active promo";
            var key = $"watch_campaign_ad_{ad.Id}";

            candidates.Add(new Alert
            {
                Type = AlertType.BigCompetitorCampaign,
                Severity = AlertSeverity.Watch,
                Title = $"Major promotional campaign detected: {compName} ({discountStr})",
                Reason = $"{compName} is running paid advertising in Egypt with promotion copy: \"{Truncate(ad.Body, 120)}\".",
                TitleAr = $"رصد حملة ترويجية كبرى: {compName} ({discountStr})",
                ReasonAr = $"يطلق {compName} إعلانات ممولة في مصر بنص ترويجي: \"{Truncate(ad.Body, 120)}\".",
                SuggestedAction = $"Monitor daily sales velocity across matched roasts to identify demand displacement.",
                SuggestedActionAr = $"متابعة معدلات المبيعات اليومية لأصناف القهوة المقابلة لرصد أي تحول في طلب العملاء.",
                CompetitorName = compName,
                AdLibraryUrl = ad.SnapshotUrl ?? ad.LinkUrl ?? "https://www.facebook.com/ads/library",
                IsBestseller = false,
                EgpImpact = 300m,
                MetricValuesJson = JsonSerializer.Serialize(new { discountText = discountStr, adId = ad.ExternalAdId }),
                IdentityKey = key
            });
        }

        // -------------------------------------------------------------
        // Optional AI Rewriting with strict number validation
        // -------------------------------------------------------------
        // Our own ads priced above a competitor's ad for the same matched product. These come from our
        // ads only; competitor alerts above never count them.
        var ownComparison = await OurAdsComparisonLoader.BuildAsync(_dbContext, ct);
        foreach (var row in ownComparison.Where(r => r.OurAdIsMoreExpensive))
        {
            var gapPct = Math.Round(((row.OurPrice!.Value - row.CompetitorPrice!.Value) / row.CompetitorPrice.Value) * 100m, 1);
            var gap = Math.Round(row.OurPrice.Value - row.CompetitorPrice.Value, 2);
            candidates.Add(new Alert
            {
                Type = AlertType.OurAdMoreExpensiveThanCompetitorAd,
                Severity = AlertSeverity.Urgent,
                Title = $"We advertise {row.OurProductTitle} above {row.CompetitorName}",
                Reason = $"Our ad prices it at {row.OurPrice:F2} EGP, {gapPct:F1}% above {row.CompetitorName}'s ad at {row.CompetitorPrice:F2} EGP for the same product.",
                SuggestedAction = "Review our advertised price, or match the competitor's advertised price.",
                ReferenceProductId = row.OurProductId,
                ReferenceProductTitle = row.OurProductTitle,
                CompetitorProductId = row.CompetitorProductId,
                CompetitorProductTitle = row.CompetitorProductTitle,
                CompetitorName = row.CompetitorName,
                AdLibraryUrl = row.CompetitorAdUrl ?? row.OurAdUrl ?? "https://www.facebook.com/ads/library",
                EgpImpact = gap,
                MetricValuesJson = JsonSerializer.Serialize(new { gapPercent = gapPct, ourPrice = row.OurPrice, competitorPrice = row.CompetitorPrice }),
                IdentityKey = $"own_ad_higher_{row.OurProductId}_{row.CompetitorId}"
            });
        }

        await ApplyOptionalAiRewritingAsync(candidates, ct);

        // -------------------------------------------------------------
        // Deduplicate and Persist Lifecycle
        // -------------------------------------------------------------
        var existingAlerts = await _dbContext.Alerts.ToListAsync(ct);
        var existingByKey = existingAlerts.ToDictionary(a => a.IdentityKey);

        var finalAlerts = new List<Alert>();

        foreach (var cand in candidates)
        {
            if (existingByKey.TryGetValue(cand.IdentityKey, out var existing))
            {
                // Check Lifecycle states: Done / Snooze / Dismiss
                var newGap = ExtractPrimaryMetric(cand.MetricValuesJson);
                var oldGap = ExtractPrimaryMetric(existing.MetricValuesJson);
                var diffPoints = Math.Abs(newGap - oldGap);
                var isMaterialChange = diffPoints >= settings.MaterialChangePercentPoints;

                if (existing.Status == AlertStatus.Snoozed)
                {
                    if (existing.SnoozedUntil.HasValue && existing.SnoozedUntil.Value <= DateTimeOffset.UtcNow)
                    {
                        existing.Status = AlertStatus.Active;
                        existing.SnoozedUntil = null;
                        _dbContext.AlertHistories.Add(new AlertHistory
                        {
                            AlertId = existing.Id,
                            Action = "Reopened",
                            Note = "7-day snooze period expired.",
                            PerformedBy = "system",
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                    else if (isMaterialChange)
                    {
                        existing.Status = AlertStatus.Active;
                        existing.SnoozedUntil = null;
                        _dbContext.AlertHistories.Add(new AlertHistory
                        {
                            AlertId = existing.Id,
                            Action = "Reopened",
                            Note = $"Reopened: numbers changed materially by {diffPoints:F1} points.",
                            PerformedBy = "system",
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
                else if (existing.Status == AlertStatus.Dismissed && isMaterialChange)
                {
                    existing.Status = AlertStatus.Active;
                    _dbContext.AlertHistories.Add(new AlertHistory
                    {
                        AlertId = existing.Id,
                        Action = "Reopened",
                        Note = $"Reopened: numbers changed materially by {diffPoints:F1} points after dismissal.",
                        PerformedBy = "system",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                // Update contents
                existing.Title = cand.Title;
                existing.Reason = cand.Reason;
                existing.TitleAr = cand.TitleAr;
                existing.ReasonAr = cand.ReasonAr;
                existing.SuggestedAction = cand.SuggestedAction;
                existing.SuggestedActionAr = cand.SuggestedActionAr;
                existing.EgpImpact = cand.EgpImpact;
                existing.MetricValuesJson = cand.MetricValuesJson;
                existing.IsBestseller = cand.IsBestseller;
                existing.AdLibraryUrl = cand.AdLibraryUrl;
                existing.ReferenceProductUrl = cand.ReferenceProductUrl;
                existing.CompetitorProductUrl = cand.CompetitorProductUrl;
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                finalAlerts.Add(existing);
            }
            else
            {
                cand.CreatedAt = DateTimeOffset.UtcNow;
                _dbContext.Alerts.Add(cand);
                _dbContext.AlertHistories.Add(new AlertHistory
                {
                    AlertId = cand.Id,
                    Action = "Created",
                    Note = "Auto-generated by data intelligence engine.",
                    PerformedBy = "system",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                finalAlerts.Add(cand);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Alert sync completed. Total alerts: {Count}", finalAlerts.Count);

        return finalAlerts.Select(ToDto).ToList();
    }

    private async Task ApplyOptionalAiRewritingAsync(List<Alert> candidates, CancellationToken ct)
    {
        try
        {
            var setting = await _dbContext.AiSettings.FirstOrDefaultAsync(ct);
            if (setting == null || !setting.IsEnabled) return;

            // Prepare facts for top 10 urgent/opportunity alerts
            var targetAlerts = candidates.Take(10).ToList();
            var factItems = new List<AttentionFactItem>();

            foreach (var a in targetAlerts)
            {
                var nums = ExtractNumbersFromText(a.Reason);
                factItems.Add(new AttentionFactItem(
                    a.Id.ToString(),
                    a.Type.ToString(),
                    a.ReferenceProductTitle ?? a.Title,
                    null,
                    a.CompetitorName ?? "Competitor",
                    a.EgpImpact,
                    0,
                    a.EgpImpact,
                    null,
                    null,
                    a.ReferenceProductUrl,
                    nums,
                    a.Title,
                    a.Reason,
                    a.TitleAr,
                    a.ReasonAr));
            }

            var aiResults = await _aiClient.RankAndPhraseAttentionItemsAsync(factItems, ct);
            foreach (var res in aiResults)
            {
                var alert = targetAlerts.FirstOrDefault(a => a.Id.ToString() == res.Id);
                if (alert != null)
                {
                    // Strict number check
                    var factItem = factItems.First(f => f.Id == res.Id);
                    var valid = AiFactNumberValidator.ValidateNumbers(factItem, res.Headline, res.HeadlineAr, res.Explanation, res.ExplanationAr);

                    if (valid)
                    {
                        if (!string.IsNullOrWhiteSpace(res.Headline))
                            alert.Title = res.Headline;
                        if (!string.IsNullOrWhiteSpace(res.Explanation))
                            alert.Reason = res.Explanation;
                        if (!string.IsNullOrWhiteSpace(res.HeadlineAr))
                            alert.TitleAr = res.HeadlineAr;
                        if (!string.IsNullOrWhiteSpace(res.ExplanationAr))
                            alert.ReasonAr = res.ExplanationAr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Optional AI rewriting skipped due to error: {Msg}", ex.Message);
        }
    }

    private async Task<HashSet<string>> GetBestsellerExternalIdsAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync("lazybean_bestsellers", async token =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var json = await client.GetStringAsync("https://api.lazzybean.com/api/products?special=bestseller&limit=100", token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataEl))
                {
                    JsonElement arrayEl = default;
                    if (dataEl.ValueKind == JsonValueKind.Array) arrayEl = dataEl;
                    else if (dataEl.TryGetProperty("data", out var innerArr) && innerArr.ValueKind == JsonValueKind.Array) arrayEl = innerArr;

                    if (arrayEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arrayEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("id", out var idEl))
                                set.Add(idEl.GetString() ?? "");
                            if (item.TryGetProperty("name", out var nameEl))
                                set.Add(nameEl.GetString() ?? "");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not fetch Lazy Bean bestsellers live: {Msg}", ex.Message);
            }
            return set;
        }, TimeSpan.FromHours(2), null, ct) ?? [];
    }

    private static decimal ExtractPrimaryMetric(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("gapPercent", out var gp)) return gp.GetDecimal();
            if (root.TryGetProperty("dropPercent", out var dp)) return dp.GetDecimal();
            if (root.TryGetProperty("marginRoomPercent", out var mp)) return mp.GetDecimal();
            if (root.TryGetProperty("competitorPrice", out var cp)) return cp.GetDecimal();
        }
        catch
        {
        }
        return 0m;
    }

    private static decimal[] ExtractNumbersFromText(string text)
    {
        var matches = Regex.Matches(text, @"\b\d+(\.\d+)?\b");
        var list = new List<decimal>();
        foreach (Match m in matches)
        {
            if (decimal.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                list.Add(d);
        }
        return list.ToArray();
    }

    private static string Truncate(string? str, int max)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= max ? str : str.Substring(0, max) + "...";
    }

    private static AlertDto ToDto(Alert a) => new(
        a.Id,
        a.Type.ToString(),
        a.Severity.ToString(),
        a.Title,
        a.Reason,
        a.TitleAr,
        a.ReasonAr,
        a.SuggestedAction,
        a.SuggestedActionAr,
        a.ReferenceProductId,
        a.ReferenceProductTitle,
        a.ReferenceProductUrl,
        a.CompetitorProductId,
        a.CompetitorProductTitle,
        a.CompetitorProductUrl,
        a.CompetitorName,
        a.AdLibraryUrl,
        a.IsBestseller,
        a.EgpImpact,
        a.MetricValuesJson,
        a.Status.ToString(),
        a.SnoozedUntil,
        a.DismissNote,
        a.ResolvedAt,
        a.CreatedAt,
        a.UpdatedAt);
}
