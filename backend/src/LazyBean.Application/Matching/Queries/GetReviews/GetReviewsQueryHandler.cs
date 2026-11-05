using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Matching.Queries.GetReviews;

public class GetReviewsQueryHandler(IReadDbContext readContext)
    : IRequestHandler<GetReviewsQuery, ReviewQueueResponseDto>
{
    public async Task<ReviewQueueResponseDto> Handle(GetReviewsQuery request, CancellationToken ct)
    {
        var targetStatus = request.Status ?? ReviewStatus.Pending;

        var query = readContext.Query<ReviewItem>()
            .Where(r => r.Kind == ReviewItemKind.ProductMatch && r.Status == targetStatus);

        var totalCount = await query.CountAsync(ct);

        var allItems = await query.ToListAsync(ct);

        var parsedList = new List<(ReviewItem Item, int CategoryPriority, decimal Score, ReviewMatchItemDto Dto)>();

        foreach (var item in allItems)
        {
            try
            {
                using var doc = JsonDocument.Parse(item.PayloadJson);
                var root = doc.RootElement;

                var compVariantId = root.TryGetProperty("CompetitorVariantId", out var cvi) ? cvi.GetGuid() : Guid.Empty;
                var refVariantId = root.TryGetProperty("ReferenceVariantId", out var rvi) ? rvi.GetGuid() : Guid.Empty;
                var compProductId = root.TryGetProperty("CompetitorProductId", out var cpi) ? cpi.GetGuid() : Guid.Empty;
                var refProductId = root.TryGetProperty("ReferenceProductId", out var rpi) ? rpi.GetGuid() : Guid.Empty;

                var compExtId = root.TryGetProperty("CompetitorExternalId", out var cei) && cei.ValueKind == JsonValueKind.String ? cei.GetString() : null;
                var refExtId = root.TryGetProperty("ReferenceExternalId", out var rei) && rei.ValueKind == JsonValueKind.String ? rei.GetString() : null;

                var compTitle = root.TryGetProperty("CompetitorTitle", out var ctProp) && ctProp.ValueKind == JsonValueKind.String ? ctProp.GetString() ?? "" : "";
                var refTitle = root.TryGetProperty("ReferenceTitle", out var rtProp) && rtProp.ValueKind == JsonValueKind.String ? rtProp.GetString() ?? "" : "";
                var compVendor = root.TryGetProperty("CompetitorVendor", out var cvProp) && cvProp.ValueKind == JsonValueKind.String ? cvProp.GetString() : null;
                var refVendor = root.TryGetProperty("ReferenceVendor", out var rvProp) && rvProp.ValueKind == JsonValueKind.String ? rvProp.GetString() : null;
                var compBrand = root.TryGetProperty("CompetitorBrand", out var cbProp) && cbProp.ValueKind == JsonValueKind.String ? cbProp.GetString() : compVendor;
                var refBrand = root.TryGetProperty("ReferenceBrand", out var rbProp) && rbProp.ValueKind == JsonValueKind.String ? rbProp.GetString() : null;
                var compCategory = root.TryGetProperty("CompetitorCategory", out var ccProp) && ccProp.ValueKind == JsonValueKind.String ? ccProp.GetString() : null;
                var refCategory = root.TryGetProperty("ReferenceCategory", out var rcProp) && rcProp.ValueKind == JsonValueKind.String ? rcProp.GetString() : null;
                var canonicalCategory = root.TryGetProperty("CanonicalCategory", out var canCat) && canCat.ValueKind == JsonValueKind.String ? canCat.GetString() : refCategory;
                var compPrice = root.TryGetProperty("CompetitorPrice", out var cpProp) && cpProp.ValueKind == JsonValueKind.Number && cpProp.TryGetDecimal(out var cpVal) ? cpVal : 0m;
                var refPrice = root.TryGetProperty("ReferencePrice", out var rpProp) && rpProp.ValueKind == JsonValueKind.Number && rpProp.TryGetDecimal(out var rpVal) ? rpVal : 0m;
                var score = root.TryGetProperty("Score", out var sProp) && sProp.ValueKind == JsonValueKind.Number && sProp.TryGetDecimal(out var sVal) ? sVal : 0m;
                var canonicalUrl = root.TryGetProperty("CanonicalUrl", out var urlProp) && urlProp.ValueKind == JsonValueKind.String ? urlProp.GetString() : null;
                var refUrl = root.TryGetProperty("ReferenceUrl", out var refUrlProp) && refUrlProp.ValueKind == JsonValueKind.String ? refUrlProp.GetString() : null;

                var compWeight = root.TryGetProperty("CompetitorWeightGrams", out var cwg) && cwg.ValueKind == JsonValueKind.Number && cwg.TryGetInt32(out var cwgVal) ? (int?)cwgVal : null;
                var refWeight = root.TryGetProperty("ReferenceWeightGrams", out var rwg) && rwg.ValueKind == JsonValueKind.Number && rwg.TryGetInt32(out var rwgVal) ? (int?)rwgVal : null;
                var compUnit = root.TryGetProperty("CompetitorUnitCount", out var cuc) && cuc.ValueKind == JsonValueKind.Number && cuc.TryGetInt32(out var cucVal) ? (int?)cucVal : null;
                var refUnit = root.TryGetProperty("ReferenceUnitCount", out var ruc) && ruc.ValueKind == JsonValueKind.Number && ruc.TryGetInt32(out var rucVal) ? (int?)rucVal : null;

                var compCapsule = root.TryGetProperty("CompetitorCapsuleSystem", out var ccs) && ccs.ValueKind == JsonValueKind.String ? ccs.GetString() : null;
                var refCapsule = root.TryGetProperty("ReferenceCapsuleSystem", out var rcs) && rcs.ValueKind == JsonValueKind.String ? rcs.GetString() : null;
                var compBeverage = root.TryGetProperty("CompetitorBeverageType", out var cbt) && cbt.ValueKind == JsonValueKind.String ? cbt.GetString() : null;
                var refBeverage = root.TryGetProperty("ReferenceBeverageType", out var rbt) && rbt.ValueKind == JsonValueKind.String ? rbt.GetString() : null;

                var aiVerdict = root.TryGetProperty("AiVerdict", out var aiv) && aiv.ValueKind == JsonValueKind.String ? aiv.GetString() : null;
                var aiConfidence = root.TryGetProperty("AiConfidence", out var aic) && aic.ValueKind == JsonValueKind.Number && aic.TryGetDecimal(out var aicVal) ? (decimal?)aicVal : null;
                var aiReason = root.TryGetProperty("AiReason", out var air) && air.ValueKind == JsonValueKind.String ? air.GetString() : null;

                var compProduct = new ReviewProductDetailsDto(
                    compProductId, compVariantId, compTitle, compVendor, compCategory,
                    compPrice, "EGP", canonicalUrl, compWeight, compUnit, compExtId, compCapsule, compBeverage, compBrand);

                var refProduct = new ReviewProductDetailsDto(
                    refProductId, refVariantId, refTitle, refVendor, refCategory,
                    refPrice, "EGP", refUrl, refWeight, refUnit, refExtId, refCapsule, refBeverage, refBrand);

                var dto = new ReviewMatchItemDto(
                    item.Id,
                    "competitor",
                    item.Reason,
                    score,
                    item.Status.ToString().ToLowerInvariant(),
                    refProduct,
                    compProduct,
                    item.CreatedAt,
                    aiVerdict,
                    aiConfidence,
                    aiReason);

                parsedList.Add((item, GetCategoryPriority(canonicalCategory), score, dto));
            }
            catch
            {
                var dto = new ReviewMatchItemDto(
                    item.Id,
                    "competitor",
                    item.Reason,
                    0m,
                    item.Status.ToString().ToLowerInvariant(),
                    null,
                    null,
                    item.CreatedAt);

                parsedList.Add((item, 99, 0m, dto));
            }
        }

        var sortedMatches = parsedList
            .OrderBy(p => p.Dto.AiVerdict != null ? 0 : 1)
            .ThenBy(p => p.CategoryPriority)
            .ThenByDescending(p => p.Score)
            .ThenByDescending(p => p.Item.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => p.Dto)
            .ToList();

        return new ReviewQueueResponseDto(sortedMatches, [], totalCount);
    }

    private static int GetCategoryPriority(string? category)
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
