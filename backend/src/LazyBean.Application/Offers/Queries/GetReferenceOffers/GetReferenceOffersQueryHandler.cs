using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Offers.Queries.GetReferenceOffers;

public class GetReferenceOffersQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetReferenceOffersQuery, ReferenceOffersResponseDto>
{
    public async Task<ReferenceOffersResponseDto> Handle(GetReferenceOffersQuery request, CancellationToken ct)
    {
        Guid? effectiveRunId = request.RunId;

        if (effectiveRunId.HasValue)
        {
            var runExists = await readContext.Query<CollectionRun>()
                .AsNoTracking()
                .AnyAsync(r => r.Id == effectiveRunId.Value, ct);

            if (!runExists)
            {
                throw new NotFoundException(nameof(CollectionRun), effectiveRunId.Value);
            }
        }
        else
        {
            // Find latest run that contains reference offers
            effectiveRunId = await (
                from o in readContext.Query<Offer>().AsNoTracking()
                join c in readContext.Query<Competitor>().AsNoTracking() on o.CompetitorId equals c.Id
                where c.IsReference
                orderby o.CreatedAt descending
                select (Guid?)o.RunId
            ).FirstOrDefaultAsync(ct);
        }

        if (!effectiveRunId.HasValue)
        {
            return new ReferenceOffersResponseDto(null, 0, []);
        }

        var query = from o in readContext.Query<Offer>().AsNoTracking()
                    where o.RunId == effectiveRunId.Value
                    join c in readContext.Query<Competitor>().AsNoTracking() on o.CompetitorId equals c.Id
                    join p in readContext.Query<Product>().AsNoTracking() on o.ProductId equals p.Id into pGroup
                    from p in pGroup.DefaultIfEmpty()
                    where c.IsReference
                    select new
                    {
                        OfferId = o.Id,
                        CompetitorId = c.Id,
                        CompetitorKey = c.Key,
                        CompetitorName = c.Name,
                        ProductId = p != null ? (Guid?)p.Id : null,
                        ProductTitle = p != null ? p.Title : "Unknown Product",
                        CanonicalUrl = p != null ? p.CanonicalUrl : "",
                        OfferType = o.Type.ToString(),
                        DetailsJson = o.DetailsJson,
                        CreatedAt = o.CreatedAt,
                        Brand = p != null ? p.Brand : null,
                        Vendor = p != null ? p.Vendor : null
                    };

        var totalCount = await query.CountAsync(ct);

        var rawList = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var offers = new List<ReferenceOfferDto>(rawList.Count);

        foreach (var item in rawList)
        {
            decimal price = 0m;
            decimal compareAt = 0m;
            decimal discountAmount = 0m;
            decimal discountPct = 0m;
            string currency = "EGP";

            try
            {
                using var doc = JsonDocument.Parse(item.DetailsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("price", out var pProp) && pProp.TryGetDecimal(out var pVal)) price = pVal;
                if (root.TryGetProperty("compareAtPrice", out var cpProp) && cpProp.TryGetDecimal(out var cpVal)) compareAt = cpVal;
                if (root.TryGetProperty("discountAmount", out var daProp) && daProp.TryGetDecimal(out var daVal)) discountAmount = daVal;
                if (root.TryGetProperty("discountPercentage", out var dpProp) && dpProp.TryGetDecimal(out var dpVal)) discountPct = dpVal;
                if (root.TryGetProperty("currency", out var cProp) && cProp.ValueKind == JsonValueKind.String) currency = cProp.GetString() ?? "EGP";
            }
            catch
            {
            }

            offers.Add(new ReferenceOfferDto(
                item.OfferId,
                item.CompetitorId,
                item.CompetitorKey,
                item.CompetitorName,
                item.ProductId,
                item.ProductTitle,
                item.CanonicalUrl,
                item.OfferType,
                price,
                compareAt,
                discountAmount,
                discountPct,
                currency,
                item.CreatedAt,
                "new",
                item.Brand,
                item.Vendor));
        }

        return new ReferenceOffersResponseDto(effectiveRunId, totalCount, offers);
    }
}
