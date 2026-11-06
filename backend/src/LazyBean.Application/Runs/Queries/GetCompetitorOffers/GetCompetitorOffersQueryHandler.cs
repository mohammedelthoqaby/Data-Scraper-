using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Runs.Queries.GetCompetitorOffers;

public class GetCompetitorOffersQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetCompetitorOffersQuery, CompetitorOffersResponseDto>
{
    public async Task<CompetitorOffersResponseDto> Handle(GetCompetitorOffersQuery request, CancellationToken ct)
    {
        var runExists = await readContext.Query<CollectionRun>()
            .AsNoTracking()
            .AnyAsync(r => r.Id == request.RunId, ct);

        if (!runExists)
        {
            throw new NotFoundException(nameof(CollectionRun), request.RunId);
        }

        var query = from o in readContext.Query<Offer>().AsNoTracking()
                    where o.RunId == request.RunId
                    join c in readContext.Query<Competitor>().AsNoTracking() on o.CompetitorId equals c.Id
                    join p in readContext.Query<Product>().AsNoTracking() on o.ProductId equals p.Id into pGroup
                    from p in pGroup.DefaultIfEmpty()
                    where !c.IsReference
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
                        Brand = p != null ? (p.Brand ?? p.Vendor) : null,
                        Vendor = p != null ? p.Vendor : null
                    };

        if (request.CompetitorId.HasValue)
        {
            query = query.Where(x => x.CompetitorId == request.CompetitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompetitorKey))
        {
            query = query.Where(x => x.CompetitorKey == request.CompetitorKey);
        }

        var rawList = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);

        var recentDiscountStartedVariantIds = (await readContext.Query<ChangeEvent>()
            .AsNoTracking()
            .Where(ce => ce.Type == ChangeEventType.DiscountStarted && ce.CreatedAt >= sevenDaysAgo && ce.VariantId != null)
            .Select(ce => ce.VariantId!.Value)
            .Distinct()
            .Take(200)
            .ToListAsync(ct)).ToHashSet();

        var recentDiscountEndedVariantIds = (await readContext.Query<ChangeEvent>()
            .AsNoTracking()
            .Where(ce => ce.Type == ChangeEventType.DiscountEnded && ce.CreatedAt >= sevenDaysAgo && ce.VariantId != null)
            .Select(ce => ce.VariantId!.Value)
            .Distinct()
            .Take(200)
            .ToListAsync(ct)).ToHashSet();

        var recentPriceChangedVariantIds = (await readContext.Query<ChangeEvent>()
            .AsNoTracking()
            .Where(ce => ce.Type == ChangeEventType.PriceChanged && ce.CreatedAt >= sevenDaysAgo && ce.VariantId != null)
            .Select(ce => ce.VariantId!.Value)
            .Distinct()
            .Take(200)
            .ToListAsync(ct)).ToHashSet();

        var countsByStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["new"] = 0,
            ["ongoing"] = 0,
            ["ended"] = 0
        };

        var offers = new List<CompetitorOfferDto>();

        foreach (var item in rawList)
        {
            decimal price = 0m;
            decimal compareAt = 0m;
            decimal discountAmount = 0m;
            decimal discountPct = 0m;
            string currency = "EGP";
            Guid? variantId = null;

            try
            {
                using var doc = JsonDocument.Parse(item.DetailsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("variantId", out var vProp) && vProp.TryGetGuid(out var vVal)) variantId = vVal;
                if (root.TryGetProperty("price", out var pProp) && pProp.TryGetDecimal(out var pVal)) price = pVal;
                if (root.TryGetProperty("compareAtPrice", out var cpProp) && cpProp.TryGetDecimal(out var cpVal)) compareAt = cpVal;
                if (root.TryGetProperty("discountAmount", out var daProp) && daProp.TryGetDecimal(out var daVal)) discountAmount = daVal;
                if (root.TryGetProperty("discountPercentage", out var dpProp) && dpProp.TryGetDecimal(out var dpVal)) discountPct = dpVal;
                if (root.TryGetProperty("currency", out var cProp) && cProp.ValueKind == JsonValueKind.String) currency = cProp.GetString() ?? "EGP";
            }
            catch
            {
            }

            string status;
            if (variantId.HasValue && recentDiscountEndedVariantIds.Contains(variantId.Value))
            {
                status = "ended";
            }
            else if (variantId.HasValue && recentDiscountStartedVariantIds.Contains(variantId.Value))
            {
                status = "new";
            }
            else
            {
                status = "ongoing";
            }

            countsByStatus[status] = countsByStatus.GetValueOrDefault(status) + 1;
            bool isRecentlyChanged = variantId.HasValue && recentPriceChangedVariantIds.Contains(variantId.Value);

            bool include;
            if (string.Equals(request.Status, "all", StringComparison.OrdinalIgnoreCase))
            {
                include = true;
            }
            else if (!string.IsNullOrWhiteSpace(request.Status))
            {
                include = string.Equals(status, request.Status, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Default: "new + recently changed"
                include = status == "new" || isRecentlyChanged;
            }

            if (include)
            {
                offers.Add(new CompetitorOfferDto(
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
                    status,
                    item.Brand,
                    item.Vendor));
            }
        }

        return new CompetitorOffersResponseDto(request.RunId, offers.Count, offers, countsByStatus);
    }
}
