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
                from o in readContext.Query<Offer>()
                join c in readContext.Query<Competitor>() on o.CompetitorId equals c.Id
                where c.IsReference
                orderby o.CreatedAt descending
                select (Guid?)o.RunId
            ).FirstOrDefaultAsync(ct);
        }

        if (!effectiveRunId.HasValue)
        {
            return new ReferenceOffersResponseDto(null, 0, []);
        }

        var query = from o in readContext.Query<Offer>()
                    where o.RunId == effectiveRunId.Value
                    join c in readContext.Query<Competitor>() on o.CompetitorId equals c.Id
                    join p in readContext.Query<Product>() on o.ProductId equals p.Id into pGroup
                    from p in pGroup.DefaultIfEmpty()
                    where c.IsReference
                    select new
                    {
                        Offer = o,
                        Competitor = c,
                        Product = p
                    };

        var rawList = await query
            .OrderByDescending(x => x.Offer.CreatedAt)
            .ToListAsync(ct);

        var offers = new List<ReferenceOfferDto>();

        foreach (var item in rawList)
        {
            decimal price = 0m;
            decimal compareAt = 0m;
            decimal discountAmount = 0m;
            decimal discountPct = 0m;
            string currency = "EGP";

            try
            {
                using var doc = JsonDocument.Parse(item.Offer.DetailsJson);
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
                item.Offer.Id,
                item.Competitor.Id,
                item.Competitor.Key,
                item.Competitor.Name,
                item.Product?.Id,
                item.Product?.Title ?? "Unknown Product",
                item.Product?.CanonicalUrl ?? "",
                item.Offer.Type.ToString(),
                price,
                compareAt,
                discountAmount,
                discountPct,
                currency,
                item.Offer.CreatedAt,
                "new",
                item.Product?.Brand ?? item.Product?.Vendor,
                item.Product?.Vendor));
        }

        return new ReferenceOffersResponseDto(effectiveRunId, offers.Count, offers);
    }
}
