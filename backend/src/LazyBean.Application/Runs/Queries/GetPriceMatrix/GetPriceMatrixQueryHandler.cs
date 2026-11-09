using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LazyBean.Application.Runs.Queries.GetPriceMatrix;

public class GetPriceMatrixQueryHandler(IReadDbContext readContext, IConfiguration? configuration = null)
    : IRequestHandler<GetPriceMatrixQuery, PriceMatrixResponseDto>
{
    private sealed record PriceObsScalar(decimal Price, decimal? CompareAtPrice, bool InStock, DateTimeOffset ObservedAt);

    public async Task<PriceMatrixResponseDto> Handle(GetPriceMatrixQuery request, CancellationToken ct)
    {
        DateTimeOffset cutoffTime;
        if (request.RunId.HasValue)
        {
            var run = await readContext.Query<CollectionRun>()
                .FirstOrDefaultAsync(r => r.Id == request.RunId.Value, ct);

            if (run is null)
            {
                throw new NotFoundException(nameof(CollectionRun), request.RunId.Value);
            }

            cutoffTime = (run.FinishedAt ?? run.StartedAt)?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        }
        else
        {
            cutoffTime = DateTimeOffset.UtcNow;
        }

        // 1. Load active product matches
        var matches = await (
            from m in readContext.Query<ProductMatch>().AsNoTracking()
            where m.Status == MatchStatus.Auto || m.Status == MatchStatus.Approved || m.Status == MatchStatus.SizeVariant
            join cv in readContext.Query<Variant>().AsNoTracking() on m.CompetitorVariantId equals cv.Id
            join cp in readContext.Query<Product>().AsNoTracking() on cv.ProductId equals cp.Id
            join c in readContext.Query<Competitor>().AsNoTracking() on cp.CompetitorId equals c.Id
            join rv in readContext.Query<Variant>().AsNoTracking() on m.ReferenceVariantId equals rv.Id
            join rp in readContext.Query<Product>().AsNoTracking() on rv.ProductId equals rp.Id
            where !c.IsReference && cp.Status == ProductStatus.Active && rp.Status == ProductStatus.Active
            select new
            {
                MatchId = m.Id,
                MatchStatus = m.Status.ToString(),
                MatchMethod = m.Method.ToString(),
                MatchReason = m.Reason,
                MatchIsBundle = m.IsBundle,
                Score = m.Score,
                CompetitorKey = c.Key,
                CompetitorName = c.Name,
                CompetitorProductId = cp.Id,
                CompetitorVariantId = cv.Id,
                CompetitorTitle = cp.Title,
                CompetitorUrl = cp.CanonicalUrl,
                CompetitorWeightGrams = cp.WeightGrams ?? cv.WeightGrams,
                CompetitorUnitCount = cp.UnitCount ?? cv.UnitCount,
                CompetitorBrand = cp.Brand ?? cp.Vendor,
                CompetitorVendor = cp.Vendor,
                CompetitorLastObservedAt = cv.LastObservedAt,
                ReferenceProductId = rp.Id,
                ReferenceVariantId = rv.Id,
                ReferenceExternalId = rp.ExternalId,
                ReferenceTitle = rp.Title,
                ReferenceCategory = rp.Category,
                ReferenceCanonicalCategory = rp.CanonicalCategory,
                ReferenceBrand = rp.Brand ?? rp.Vendor,
                ReferenceVendor = rp.Vendor,
                ReferenceWeightGrams = rp.WeightGrams ?? rv.WeightGrams,
                ReferenceUnitCount = rp.UnitCount ?? rv.UnitCount,
                ReferenceVolumeMl = (int?)null,
                ReferenceUrl = rp.CanonicalUrl,
                ReferenceLastObservedAt = rv.LastObservedAt
            }).ToListAsync(ct);

        if (matches.Count == 0)
        {
            return new PriceMatrixResponseDto(request.RunId, DateTimeOffset.UtcNow, 0, []);
        }

        var competitorVariantIds = matches.Select(m => m.CompetitorVariantId).Distinct().ToList();
        var referenceVariantIds = matches.Select(m => m.ReferenceVariantId).Distinct().ToList();

        // 2. Observations
        // If RunId is requested, Lazy Bean prices come from that run (fallback to latest <= cutoffTime)
        var runLBObservations = new Dictionary<Guid, PriceObsScalar>();
        if (request.RunId.HasValue)
        {
            var runObs = await readContext.Query<PriceObservation>()
                .AsNoTracking()
                .Where(po => referenceVariantIds.Contains(po.VariantId) && po.RunId == request.RunId.Value)
                .Select(po => new { po.VariantId, po.Price, po.CompareAtPrice, InStock = po.InStock ?? true, po.ObservedAt })
                .ToListAsync(ct);

            foreach (var o in runObs)
            {
                runLBObservations[o.VariantId] = new PriceObsScalar(o.Price, o.CompareAtPrice, o.InStock, o.ObservedAt);
            }
        }

        // Each competitor price = latest observation at or before that run (cutoffTime)
        // Also fallback for any LB variants missing in the run
        var neededVariantIds = competitorVariantIds
            .Concat(referenceVariantIds.Where(id => !runLBObservations.ContainsKey(id)))
            .Distinct()
            .ToList();

        var latestObservations = new Dictionary<Guid, PriceObsScalar>();
        if (neededVariantIds.Count > 0)
        {
            var latestList = await readContext.Query<PriceObservation>()
                .AsNoTracking()
                .Where(po => neededVariantIds.Contains(po.VariantId) && po.ObservedAt <= cutoffTime)
                .GroupBy(po => po.VariantId)
                .Select(g => g.OrderByDescending(po => po.ObservedAt)
                    .Select(po => new { po.VariantId, po.Price, po.CompareAtPrice, InStock = po.InStock ?? true, po.ObservedAt })
                    .First())
                .ToListAsync(ct);

            foreach (var obs in latestList)
            {
                latestObservations[obs.VariantId] = new PriceObsScalar(obs.Price, obs.CompareAtPrice, obs.InStock, obs.ObservedAt);
            }
        }

        PriceObsScalar? GetObservation(Guid variantId)
        {
            if (runLBObservations.TryGetValue(variantId, out var o)) return o;
            if (latestObservations.TryGetValue(variantId, out var f)) return f;
            return null;
        }

        // 3. Lazy Bean multi-vendor info - scoped only to matched reference products
        var refProductIds = matches.Select(m => m.ReferenceProductId).Distinct().ToList();
        var referenceProducts = await (
            from p in readContext.Query<Product>().AsNoTracking()
            join c in readContext.Query<Competitor>().AsNoTracking() on p.CompetitorId equals c.Id
            join v in readContext.Query<Variant>().AsNoTracking() on p.Id equals v.ProductId
            where c.IsReference && p.Status == ProductStatus.Active && refProductIds.Contains(p.Id)
            select new
            {
                ProductId = p.Id,
                VariantId = v.Id,
                p.Title,
                p.Vendor,
                p.NormalizedTitle
            }).ToListAsync(ct);

        var refProductByNormTitle = referenceProducts
            .GroupBy(p => p.NormalizedTitle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Group matches by ReferenceVariantId
        var groupedMatches = matches.GroupBy(m => m.ReferenceVariantId);
        var matrixRows = new List<PriceMatrixRowDto>();

        foreach (var group in groupedMatches)
        {
            var first = group.First();
            var refObs = GetObservation(first.ReferenceVariantId);
            var refPrice = refObs?.Price ?? 0m;
            var refInStock = refObs?.InStock ?? true;
            var refVendor = first.ReferenceVendor;
            var vendorCount = 1;

            // Check if multiple vendors sell this product on Lazy Bean
            var normTitle = TitleNormalizer.NormalizeForMatching(first.ReferenceTitle);
            if (refProductByNormTitle.TryGetValue(normTitle, out var vendorVariants) && vendorVariants.Count > 1)
            {
                vendorCount = vendorVariants.Select(v => v.Vendor).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Count();
                if (vendorCount == 0) vendorCount = vendorVariants.Count;

                var vendorObsList = vendorVariants.Select(v =>
                {
                    var o = GetObservation(v.VariantId);
                    return new { Vendor = v.Vendor, Obs = o };
                }).Where(x => x.Obs != null).ToList();

                var inStockVendors = vendorObsList.Where(x => x.Obs!.InStock == true).ToList();
                if (inStockVendors.Count > 0)
                {
                    var lowest = inStockVendors.OrderBy(x => x.Obs!.Price).First();
                    refPrice = lowest.Obs!.Price;
                    refVendor = lowest.Vendor;
                    refInStock = true;
                }
                else if (vendorObsList.Count > 0)
                {
                    var lowest = vendorObsList.OrderBy(x => x.Obs!.Price).First();
                    refPrice = lowest.Obs!.Price;
                    refVendor = lowest.Vendor;
                    refInStock = false;
                }
            }

            var compCells = new Dictionary<string, CompetitorPriceCellDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in group)
            {
                var compObs = GetObservation(match.CompetitorVariantId);
                if (compObs is null) continue;

                var compPrice = compObs.Price;
                var compCompareAt = compObs.CompareAtPrice;
                var compInStock = compObs.InStock;

                var weight = match.CompetitorWeightGrams ?? TitleNormalizer.ExtractWeightGrams(match.CompetitorTitle);
                var unit = match.CompetitorUnitCount ?? TitleNormalizer.ExtractUnitCount(match.CompetitorTitle);

                decimal? pricePerGram = weight.HasValue && weight.Value > 0
                    ? Math.Round(compPrice / weight.Value, 4)
                    : null;

                decimal? pricePerUnit = unit.HasValue && unit.Value > 0
                    ? Math.Round(compPrice / unit.Value, 4)
                    : null;

                var diff = compPrice - refPrice;
                decimal? diffPercent = refPrice > 0
                    ? Math.Round((diff / refPrice) * 100m, 2)
                    : null;

                var compObservedAt = match.CompetitorLastObservedAt ?? compObs.ObservedAt;
                var isStale = (cutoffTime - compObservedAt).TotalHours > 48;

                var cell = new CompetitorPriceCellDto(
                    match.CompetitorName,
                    match.CompetitorKey,
                    match.CompetitorProductId,
                    match.CompetitorVariantId,
                    match.CompetitorTitle,
                    match.CompetitorUrl,
                    compPrice,
                    compCompareAt,
                    pricePerGram,
                    pricePerUnit,
                    compInStock,
                    match.MatchStatus,
                    diff,
                    diffPercent,
                    compObservedAt,
                    isStale,
                    match.CompetitorBrand,
                    match.CompetitorVendor,
                    match.MatchId,
                    match.MatchMethod,
                    match.MatchReason,
                    match.MatchIsBundle);

                // If multiple variants match for same competitor, pick lowest price
                if (!compCells.TryGetValue(match.CompetitorKey, out var existingCell) || compPrice < existingCell.Price)
                {
                    compCells[match.CompetitorKey] = cell;
                }
            }

            // Exclude out of stock from cheapest competitor calculation
            var inStockCompCells = compCells.Values.Where(c => c.InStock && c.Price > 0).ToList();
            string? cheapestKey = null;
            string? cheapestName = null;
            decimal? cheapestPrice = null;
            string position;

            if (!refInStock)
            {
                position = "lazyBeanOutOfStock";
                if (inStockCompCells.Count > 0)
                {
                    var cheapest = inStockCompCells.OrderBy(c => c.Price).First();
                    cheapestKey = cheapest.CompetitorKey;
                    cheapestName = cheapest.CompetitorName;
                    cheapestPrice = cheapest.Price;
                }
            }
            else if (inStockCompCells.Count > 0)
            {
                var cheapest = inStockCompCells.OrderBy(c => c.Price).First();
                cheapestKey = cheapest.CompetitorKey;
                cheapestName = cheapest.CompetitorName;
                cheapestPrice = cheapest.Price;

                if (refPrice <= cheapest.Price)
                {
                    position = "cheapest";
                }
                else if ((refPrice - cheapest.Price) / cheapest.Price <= 0.05m)
                {
                    position = "within 5%";
                }
                else
                {
                    position = "more expensive";
                }
            }
            else
            {
                position = "cheapest";
            }

            var storefrontBase = (configuration?["Reference:StorefrontBaseUrl"] ?? "https://lazybean.co").TrimEnd('/');
            string refUrl;
            if (!string.IsNullOrWhiteSpace(first.ReferenceExternalId))
            {
                refUrl = $"{storefrontBase}/single-product/{first.ReferenceExternalId}";
            }
            else if (!string.IsNullOrWhiteSpace(first.ReferenceUrl))
            {
                refUrl = first.ReferenceUrl.Replace("https://lazzybean.com", storefrontBase, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                refUrl = string.Empty;
            }

            var row = new PriceMatrixRowDto(
                first.ReferenceProductId,
                first.ReferenceVariantId,
                first.ReferenceExternalId,
                first.ReferenceTitle,
                first.ReferenceCategory,
                first.ReferenceCanonicalCategory.ToString(),
                first.ReferenceBrand,
                first.ReferenceWeightGrams,
                first.ReferenceUnitCount,
                first.ReferenceVolumeMl,
                refUrl,
                refPrice,
                refVendor,
                vendorCount,
                refInStock,
                cheapestKey,
                cheapestName,
                cheapestPrice,
                position,
                compCells);

            matrixRows.Add(row);
        }

        // Sort by category priority, then by title
        var sortedRows = matrixRows
            .OrderBy(r => GetCategoryPriority(r.CanonicalCategory))
            .ThenBy(r => r.LazyBeanTitle)
            .ToList();

        return new PriceMatrixResponseDto(
            request.RunId,
            DateTimeOffset.UtcNow,
            sortedRows.Count,
            sortedRows);
    }

    private static int GetCategoryPriority(string category)
    {
        return category switch
        {
            "CoffeeBeans" => 1,
            "GroundCoffee" => 2,
            "TurkishCoffee" => 3,
            "InstantCoffee" => 4,
            "CapsulesNespressoOriginal" => 5,
            "CapsulesNespressoVertuo" => 6,
            "CapsulesDolceGusto" => 7,
            "CapsulesOther" => 8,
            "Equipment" => 9,
            "Accessories" => 10,
            _ => 11
        };
    }
}
