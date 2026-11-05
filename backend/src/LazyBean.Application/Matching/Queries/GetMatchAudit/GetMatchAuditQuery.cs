using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Matching.Queries.GetMatchAudit;

public record MatchAuditItemDto(
    Guid MatchId,
    string CompetitorName,
    string ReferenceTitle,
    string ReferenceUrl,
    string? ReferenceBrand,
    decimal ReferencePrice,
    string CompetitorTitle,
    string CompetitorUrl,
    string? CompetitorBrand,
    decimal CompetitorPrice,
    decimal Score,
    string Method,
    DateTimeOffset ApprovedAt,
    string? Reason);

public record GetMatchAuditQuery : IRequest<IReadOnlyList<MatchAuditItemDto>>;

public class GetMatchAuditQueryHandler(IReadDbContext readContext) : IRequestHandler<GetMatchAuditQuery, IReadOnlyList<MatchAuditItemDto>>
{
    public async Task<IReadOnlyList<MatchAuditItemDto>> Handle(GetMatchAuditQuery request, CancellationToken ct)
    {
        var aiMatches = await (
            from m in readContext.Query<ProductMatch>()
            where m.Method == MatchMethod.AI && m.Status == MatchStatus.Approved
            join cv in readContext.Query<Variant>() on m.CompetitorVariantId equals cv.Id
            join cp in readContext.Query<Product>() on cv.ProductId equals cp.Id
            join c in readContext.Query<Competitor>() on cp.CompetitorId equals c.Id
            join rv in readContext.Query<Variant>() on m.ReferenceVariantId equals rv.Id
            join rp in readContext.Query<Product>() on rv.ProductId equals rp.Id
            select new
            {
                MatchId = m.Id,
                CompetitorName = c.Name,
                ReferenceTitle = rp.Title,
                ReferenceUrl = rp.CanonicalUrl,
                ReferenceBrand = rp.Brand ?? rp.Vendor,
                ReferenceVariantId = rv.Id,
                CompetitorTitle = cp.Title,
                CompetitorUrl = cp.CanonicalUrl,
                CompetitorBrand = cp.Brand ?? cp.Vendor,
                CompetitorVariantId = cv.Id,
                Score = m.Score,
                Method = m.Method.ToString(),
                ApprovedAt = m.UpdatedAt ?? m.CreatedAt
            })
            .Take(50)
            .ToListAsync(ct);

        if (aiMatches.Count == 0) return [];

        var refVarIds = aiMatches.Select(m => m.ReferenceVariantId).Distinct().ToList();
        var compVarIds = aiMatches.Select(m => m.CompetitorVariantId).Distinct().ToList();

        var observations = await readContext.Query<PriceObservation>()
            .Where(po => refVarIds.Contains(po.VariantId) || compVarIds.Contains(po.VariantId))
            .OrderByDescending(po => po.ObservedAt)
            .ToListAsync(ct);

        var obsByVar = observations.GroupBy(po => po.VariantId).ToDictionary(g => g.Key, g => g.First().Price);

        // Take 10 items (shuffled or random)
        var rnd = new Random();
        var selected = aiMatches.OrderBy(_ => rnd.Next()).Take(10).ToList();

        var result = new List<MatchAuditItemDto>();
        foreach (var item in selected)
        {
            var refPrice = obsByVar.TryGetValue(item.ReferenceVariantId, out var rp) ? rp : 0m;
            var compPrice = obsByVar.TryGetValue(item.CompetitorVariantId, out var cp) ? cp : 0m;

            result.Add(new MatchAuditItemDto(
                item.MatchId,
                item.CompetitorName,
                item.ReferenceTitle,
                item.ReferenceUrl,
                item.ReferenceBrand,
                refPrice,
                item.CompetitorTitle,
                item.CompetitorUrl,
                item.CompetitorBrand,
                compPrice,
                item.Score,
                item.Method,
                item.ApprovedAt,
                $"Auto-approved: Brand & size match with {(int)(item.Score * 100)}% AI similarity"));
        }

        return result;
    }
}
