using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.PriceHistory.Queries.GetPriceHistory;

public class GetPriceHistoryQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetPriceHistoryQuery, PriceHistoryResponseDto>
{
    public async Task<PriceHistoryResponseDto> Handle(GetPriceHistoryQuery request, CancellationToken ct)
    {
        var targetVariantIds = new HashSet<Guid>();

        if (request.VariantId.HasValue)
        {
            targetVariantIds.Add(request.VariantId.Value);

            // If this is a reference variant, also include matched competitor variants
            var matchedVariants = await readContext.Query<ProductMatch>()
                .Where(m => m.ReferenceVariantId == request.VariantId.Value &&
                           (m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant))
                .Select(m => m.CompetitorVariantId)
                .ToListAsync(ct);

            foreach (var mv in matchedVariants) targetVariantIds.Add(mv);
        }
        else if (request.ProductId.HasValue)
        {
            var productVariants = await readContext.Query<Variant>()
                .Where(v => v.ProductId == request.ProductId.Value)
                .Select(v => v.Id)
                .ToListAsync(ct);

            foreach (var pv in productVariants) targetVariantIds.Add(pv);

            // Check if product is reference; if so, include matched competitor variants
            var isReference = await readContext.Query<Product>()
                .Where(p => p.Id == request.ProductId.Value)
                .Join(readContext.Query<Competitor>(), p => p.CompetitorId, c => c.Id, (p, c) => c.IsReference)
                .FirstOrDefaultAsync(ct);

            if (isReference)
            {
                var matchedCompVariants = await readContext.Query<ProductMatch>()
                    .Where(m => productVariants.Contains(m.ReferenceVariantId) &&
                               (m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant))
                    .Select(m => m.CompetitorVariantId)
                    .ToListAsync(ct);

                foreach (var mv in matchedCompVariants) targetVariantIds.Add(mv);
            }
        }

        var query = from po in readContext.Query<PriceObservation>()
                    join v in readContext.Query<Variant>() on po.VariantId equals v.Id
                    join p in readContext.Query<Product>() on v.ProductId equals p.Id
                    join c in readContext.Query<Competitor>() on p.CompetitorId equals c.Id
                    select new
                    {
                        Observation = po,
                        Variant = v,
                        Product = p,
                        Competitor = c
                    };

        if (targetVariantIds.Count > 0)
        {
            query = query.Where(x => targetVariantIds.Contains(x.Observation.VariantId));
        }

        if (request.CompetitorId.HasValue)
        {
            query = query.Where(x => x.Competitor.Id == request.CompetitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompetitorKey))
        {
            query = query.Where(x => x.Competitor.Key == request.CompetitorKey);
        }

        if (request.From.HasValue)
        {
            query = query.Where(x => x.Observation.ObservedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(x => x.Observation.ObservedAt <= request.To.Value);
        }

        var total = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : (request.PageSize > 500 ? 500 : request.PageSize);

        var orderedQuery = query.OrderBy(x => x.Observation.ObservedAt);

        IQueryable<dynamic> pagedQuery;
        if (request.All)
        {
            pagedQuery = orderedQuery.Select(x => (dynamic)x);
        }
        else
        {
            pagedQuery = orderedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => (dynamic)x);
        }

        var results = await orderedQuery
            .ApplyPagination(request.All, page, pageSize)
            .Select(x => new PriceHistoryPointDto(
                x.Observation.Id,
                x.Observation.ObservedAt,
                x.Observation.RunId,
                x.Competitor.Id,
                x.Competitor.Key,
                x.Competitor.Name,
                x.Product.Id,
                x.Product.Title,
                x.Product.ExternalId,
                x.Variant.Id,
                x.Variant.Title,
                x.Observation.Price,
                x.Observation.CompareAtPrice,
                x.Observation.InStock,
                x.Observation.Currency))
            .ToListAsync(ct);

        return new PriceHistoryResponseDto(
            total,
            request.All ? 1 : page,
            request.All ? (total == 0 ? 1 : total) : pageSize,
            results);
    }
}

internal static class QueryableExtensions
{
    public static IQueryable<T> ApplyPagination<T>(this IQueryable<T> query, bool all, int page, int pageSize)
    {
        if (all) return query;
        return query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
