using System.Text.Json;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Overview.Queries.GetAttentionItems;

public record GetAttentionItemsQuery : IRequest<IReadOnlyList<RankedAttentionItem>>;

public class GetAttentionItemsQueryHandler : IRequestHandler<GetAttentionItemsQuery, IReadOnlyList<RankedAttentionItem>>
{
    private sealed record VariantObsInfo(decimal Price, bool? InStock);

    private readonly IReadDbContext _readContext;
    private readonly IAiClient _aiClient;

    public GetAttentionItemsQueryHandler(IReadDbContext readContext, IAiClient aiClient)
    {
        _readContext = readContext;
        _aiClient = aiClient;
    }

    public async Task<IReadOnlyList<RankedAttentionItem>> Handle(GetAttentionItemsQuery request, CancellationToken ct)
    {
        var facts = new List<AttentionFactItem>();

        // 1. Query active product matches with variants and competitor
        var matches = await (
            from m in _readContext.Query<ProductMatch>().AsNoTracking()
            where m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant
            join cv in _readContext.Query<Variant>().AsNoTracking() on m.CompetitorVariantId equals cv.Id
            join cp in _readContext.Query<Product>().AsNoTracking() on cv.ProductId equals cp.Id
            join c in _readContext.Query<Competitor>().AsNoTracking() on cp.CompetitorId equals c.Id
            join rv in _readContext.Query<Variant>().AsNoTracking() on m.ReferenceVariantId equals rv.Id
            join rp in _readContext.Query<Product>().AsNoTracking() on rv.ProductId equals rp.Id
            where !c.IsReference && cp.Status == ProductStatus.Active && rp.Status == ProductStatus.Active
            select new
            {
                MatchId = m.Id,
                CompetitorName = c.Name,
                ReferenceTitle = rp.Title,
                ReferenceBrand = rp.Brand ?? rp.Vendor,
                ReferenceUrl = rp.CanonicalUrl,
                ReferenceVariantId = rv.Id,
                CompetitorVariantId = cv.Id
            }).Take(50).ToListAsync(ct);

        if (matches.Count > 0)
        {
            var refVarIds = matches.Select(m => m.ReferenceVariantId).Distinct().ToList();
            var compVarIds = matches.Select(m => m.CompetitorVariantId).Distinct().ToList();

            var latestObs = await _readContext.Query<PriceObservation>()
                .AsNoTracking()
                .Where(po => refVarIds.Contains(po.VariantId) || compVarIds.Contains(po.VariantId))
                .GroupBy(po => po.VariantId)
                .Select(g => g.OrderByDescending(po => po.ObservedAt)
                    .Select(po => new { po.VariantId, po.Price, po.InStock })
                    .First())
                .ToListAsync(ct);

            var obsByVariant = latestObs
                .ToDictionary(x => x.VariantId, x => new VariantObsInfo(x.Price, x.InStock));

            foreach (var m in matches)
            {
                obsByVariant.TryGetValue(m.ReferenceVariantId, out var refObs);
                obsByVariant.TryGetValue(m.CompetitorVariantId, out var compObs);

                if (refObs == null || compObs == null || refObs.Price <= 0 || compObs.Price <= 0)
                    continue;

                var refPrice = Math.Round(refObs.Price, 2);
                var compPrice = Math.Round(compObs.Price, 2);

                // Fact 1: Price Gap (Lazy Bean more expensive)
                if (refPrice > compPrice)
                {
                    var diff = Math.Round(refPrice - compPrice, 2);
                    facts.Add(new AttentionFactItem(
                        Id: $"gap_{m.MatchId}",
                        Type: "price_gap",
                        ProductTitle: m.ReferenceTitle,
                        Brand: m.ReferenceBrand,
                        CompetitorName: m.CompetitorName,
                        LazyBeanPrice: refPrice,
                        CompetitorPrice: compPrice,
                        Difference: diff,
                        DiscountPercent: null,
                        AdCount: null,
                        ProductUrl: m.ReferenceUrl,
                        Numbers: [refPrice, compPrice, diff],
                        FallbackHeadline: $"Lazy Bean is {diff:F2} EGP more expensive on {m.ReferenceTitle}",
                        FallbackExplanation: $"Current Lazy Bean price is {refPrice:F2} EGP while {m.CompetitorName} sells it for {compPrice:F2} EGP (gap of {diff:F2} EGP).",
                        FallbackHeadlineAr: $"سعر ليزي بين أعلى بمقدار {diff:F2} ج.م على {m.ReferenceTitle}",
                        FallbackExplanationAr: $"سعر ليزي بين الحالي {refPrice:F2} ج.م بينما يبيعه {m.CompetitorName} بسعر {compPrice:F2} ج.م (فارق {diff:F2} ج.م)."));
                }

                // Fact 4: Lazy Bean out of stock while competitor in stock
                if (refObs.InStock == false && compObs.InStock == true)
                {
                    facts.Add(new AttentionFactItem(
                        Id: $"stock_{m.MatchId}",
                        Type: "out_of_stock",
                        ProductTitle: m.ReferenceTitle,
                        Brand: m.ReferenceBrand,
                        CompetitorName: m.CompetitorName,
                        LazyBeanPrice: refPrice,
                        CompetitorPrice: compPrice,
                        Difference: null,
                        DiscountPercent: null,
                        AdCount: null,
                        ProductUrl: m.ReferenceUrl,
                        Numbers: [refPrice, compPrice],
                        FallbackHeadline: $"{m.ReferenceTitle} is out of stock at Lazy Bean",
                        FallbackExplanation: $"Product is currently out of stock at Lazy Bean but available in stock at {m.CompetitorName} for {compPrice:F2} EGP.",
                        FallbackHeadlineAr: $"نفاد مخزون {m.ReferenceTitle} لدى ليزي بين",
                        FallbackExplanationAr: $"المنتج غير متوفر حالياً لدى ليزي بين ولكنه متوفر لدى {m.CompetitorName} بسعر {compPrice:F2} ج.م."));
                }
            }
        }

        // Fact 2: Competitor Discounts on matched products
        var recentOffers = await (
            from o in _readContext.Query<Offer>()
            join c in _readContext.Query<Competitor>() on o.CompetitorId equals c.Id
            join p in _readContext.Query<Product>() on o.ProductId equals p.Id into pGrp
            from p in pGrp.DefaultIfEmpty()
            where !c.IsReference
            orderby o.CreatedAt descending
            select new
            {
                o.Id,
                CompetitorName = c.Name,
                ProductTitle = p != null ? p.Title : "Coffee item",
                Brand = p != null ? p.Brand : null,
                CanonicalUrl = p != null ? p.CanonicalUrl : "",
                o.DetailsJson
            }).Take(15).ToListAsync(ct);

        foreach (var off in recentOffers)
        {
            try
            {
                using var doc = JsonDocument.Parse(off.DetailsJson);
                var root = doc.RootElement;
                var discountPct = root.TryGetProperty("discountPercentage", out var dp) && dp.TryGetDecimal(out var dpVal) ? dpVal : 0m;
                var price = root.TryGetProperty("price", out var pr) && pr.TryGetDecimal(out var prVal) ? prVal : 0m;
                var compareAt = root.TryGetProperty("compareAtPrice", out var cp) && cp.TryGetDecimal(out var cpVal) ? cpVal : price;

                if (discountPct > 0 && price > 0)
                {
                    var roundedDisc = Math.Round(discountPct, 0);
                    var roundedPrice = Math.Round(price, 2);
                    var roundedComp = Math.Round(compareAt, 2);

                    facts.Add(new AttentionFactItem(
                        Id: $"discount_{off.Id}",
                        Type: "competitor_discount",
                        ProductTitle: off.ProductTitle,
                        Brand: off.Brand,
                        CompetitorName: off.CompetitorName,
                        LazyBeanPrice: roundedComp,
                        CompetitorPrice: roundedPrice,
                        Difference: Math.Round(roundedComp - roundedPrice, 2),
                        DiscountPercent: roundedDisc,
                        AdCount: null,
                        ProductUrl: off.CanonicalUrl,
                        Numbers: [roundedPrice, roundedComp, roundedDisc],
                        FallbackHeadline: $"{off.CompetitorName} discounted {off.ProductTitle} by {roundedDisc:F0}%",
                        FallbackExplanation: $"New promotional discount of {roundedDisc:F0}% brings price down from {roundedComp:F2} EGP to {roundedPrice:F2} EGP at {off.CompetitorName}.",
                        FallbackHeadlineAr: $"خصم بنسبة {roundedDisc:F0}% من {off.CompetitorName} على {off.ProductTitle}",
                        FallbackExplanationAr: $"تخفيض ترويجي جديد بنسبة {roundedDisc:F0}% يخفض السعر من {roundedComp:F2} ج.م إلى {roundedPrice:F2} ج.م لدى {off.CompetitorName}."));
                }
            }
            catch
            {
            }
        }

        // Fact 3: Competitor Ads on products where Lazy Bean is more expensive
        var activeAds = await _readContext.Query<Ad>()
            .Where(a => a.Status == AdStatus.Active)
            .Take(10)
            .ToListAsync(ct);

        if (activeAds.Count > 0)
        {
            var adCount = activeAds.Count;
            var gapFact = facts.FirstOrDefault(f => f.Type == "price_gap");
            if (gapFact != null)
            {
                facts.Add(new AttentionFactItem(
                    Id: $"ad_activity_{activeAds[0].Id}",
                    Type: "competitor_ad",
                    ProductTitle: gapFact.ProductTitle,
                    Brand: gapFact.Brand,
                    CompetitorName: gapFact.CompetitorName,
                    LazyBeanPrice: gapFact.LazyBeanPrice,
                    CompetitorPrice: gapFact.CompetitorPrice,
                    Difference: gapFact.Difference,
                    DiscountPercent: null,
                    AdCount: adCount,
                    ProductUrl: gapFact.ProductUrl,
                    Numbers: [gapFact.LazyBeanPrice, gapFact.CompetitorPrice, adCount],
                    FallbackHeadline: $"{gapFact.CompetitorName} running {adCount} ads while pricing below Lazy Bean",
                    FallbackExplanation: $"{gapFact.CompetitorName} is actively running {adCount} ads in Egypt while pricing {gapFact.ProductTitle} at {gapFact.CompetitorPrice:F2} EGP vs Lazy Bean's {gapFact.LazyBeanPrice:F2} EGP.",
                    FallbackHeadlineAr: $"حملة إعلانية نشطة ({adCount} إعلانات) من {gapFact.CompetitorName} بأسعار أقل من ليزي بين",
                    FallbackExplanationAr: $"يقوم {gapFact.CompetitorName} بالإعلان في مصر ({adCount} إعلانات) عن {gapFact.ProductTitle} بسعر {gapFact.CompetitorPrice:F2} ج.م مقارنة بسعر {gapFact.LazyBeanPrice:F2} ج.م لدى ليزي بين."));
            }
        }

        var topFacts = facts
            .GroupBy(f => f.Type)
            .SelectMany(g => g.Take(3))
            .Take(10)
            .ToList();

        return await _aiClient.RankAndPhraseAttentionItemsAsync(topFacts, ct);
    }
}
