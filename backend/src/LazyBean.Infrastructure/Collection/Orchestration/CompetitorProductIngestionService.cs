using System.Diagnostics;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Classification;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collection.Validation;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class CompetitorProductIngestionService(
    IDbContextFactory<LazyBeanDbContext> dbContextFactory,
    IProductClassifier classifier,
    ICollectionProgressTracker? progressTracker,
    ILogger<CompetitorProductIngestionService> logger) : ICompetitorProductIngestionService
{
    public async Task<IngestBatchResult> IngestBatchAsync(
        Guid runId,
        string competitorSlug,
        int pageNumber,
        int? totalPages,
        IReadOnlyList<RawProduct> products,
        CancellationToken ct = default)
    {
        if (products.Count == 0)
        {
            return new IngestBatchResult(0, 0, 0, 0);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var competitor = await db.Competitors.FirstOrDefaultAsync(c => c.Key == competitorSlug, ct)
            ?? throw new InvalidOperationException($"Competitor with slug '{competitorSlug}' not found.");

        var competitorRun = await db.CompetitorRuns.FirstOrDefaultAsync(
            r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);

        if (competitorRun == null)
        {
            competitorRun = new CompetitorRun
            {
                RunId = runId,
                CompetitorId = competitor.Id,
                Status = CompetitorRunStatus.Running
            };
            db.CompetitorRuns.Add(competitorRun);
            await db.SaveChangesAsync(ct);
        }

        var productLookup = await db.Products
            .Where(p => p.CompetitorId == competitor.Id)
            .ToDictionaryAsync(p => p.ExternalId, p => p.Id, ct);

        var variantLookup = await (from v in db.Variants
                                   join p in db.Products on v.ProductId equals p.Id
                                   where p.CompetitorId == competitor.Id
                                   select new { v.ProductId, v.ExternalId, v.Id })
            .ToDictionaryAsync(v => (v.ProductId, v.ExternalId), v => v.Id, ct);

        var existingProductIds = products
            .Where(b => productLookup.ContainsKey(b.ExternalId))
            .Select(b => productLookup[b.ExternalId])
            .Distinct()
            .ToList();

        var existingProducts = existingProductIds.Count > 0
            ? await db.Products.Where(p => existingProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<Guid, Product>();

        var existingVariantIds = products
            .SelectMany(b => b.Variants.Select(v =>
                productLookup.TryGetValue(b.ExternalId, out var pid) && variantLookup.TryGetValue((pid, v.ExternalId), out var vid)
                    ? vid
                    : Guid.Empty))
            .Where(vid => vid != Guid.Empty)
            .Distinct()
            .ToList();

        var existingVariants = existingVariantIds.Count > 0
            ? await db.Variants.Where(v => existingVariantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct)
            : new Dictionary<Guid, Variant>();

        var latestObsList = existingVariantIds.Count > 0
            ? await db.PriceObservations
                .Where(po => existingVariantIds.Contains(po.VariantId) && po.RunId != runId)
                .GroupBy(po => po.VariantId)
                .Select(g => g.OrderByDescending(po => po.ObservedAt).First())
                .ToListAsync(ct)
            : new List<PriceObservation>();

        var latestObsLookup = latestObsList.ToDictionary(po => po.VariantId);

        var existingRunObs = existingVariantIds.Count > 0
            ? await db.PriceObservations
                .Where(po => existingVariantIds.Contains(po.VariantId) && po.RunId == runId)
                .ToDictionaryAsync(po => po.VariantId, ct)
            : new Dictionary<Guid, PriceObservation>();

        var existingOfferProductIds = await db.Offers
            .Where(o => o.RunId == runId && o.CompetitorId == competitor.Id)
            .Select(o => o.ProductId)
            .ToHashSetAsync(ct);

        var pageVariantIdsToTouch = new HashSet<Guid>();

        var productsSaved = 0;
        var variantsSaved = 0;
        var observationsRecorded = 0;
        var changeEventsRecorded = 0;

        foreach (var raw in products)
        {
            var decodedTitle = TitleNormalizer.Decode(raw.Title);
            Product product;
            var isNewProduct = !productLookup.TryGetValue(raw.ExternalId, out var existingProductId);

            if (!isNewProduct && existingProducts.TryGetValue(existingProductId, out var existingProd))
            {
                product = existingProd;
            }
            else
            {
                product = new Product { CompetitorId = competitor.Id, ExternalId = raw.ExternalId };
                db.Products.Add(product);
                productLookup[raw.ExternalId] = product.Id;
                existingProducts[product.Id] = product;
                isNewProduct = true;
                productsSaved++;
            }

            product.Title = decodedTitle;
            product.NormalizedTitle = TitleNormalizer.NormalizeForMatching(raw.Title);
            product.CanonicalUrl = raw.CanonicalUrl;
            if (!string.IsNullOrWhiteSpace(raw.ImageUrl))
            {
                product.ImageUrl = raw.ImageUrl;
            }
            product.Handle = !string.IsNullOrWhiteSpace(raw.Handle)
                ? raw.Handle
                : CreativeHashHelper.ExtractProductHandle(raw.CanonicalUrl);
            product.Category = raw.Category;
            product.ProductType = raw.ProductType;
            product.VendorId = raw.VendorId;
            product.Vendor = raw.Vendor;
            product.Brand = raw.Brand ?? raw.Vendor;
            product.Tags = raw.Tags;
            if (!string.IsNullOrWhiteSpace(raw.Roast))
            {
                product.Roast = raw.Roast;
            }

            // Classify product
            var classification = classifier.Classify(decodedTitle, raw.ProductType, raw.Vendor, raw.Tags, raw.Category);
            product.CanonicalCategory = classification.Category;
            product.ClassificationConfidence = classification.Confidence;
            product.MatchedRule = classification.MatchedRule;

            product.WeightGrams = raw.WeightGrams is > 0
                ? raw.WeightGrams
                : TitleNormalizer.ExtractWeightGrams(raw.Title);
            product.UnitCount = TitleNormalizer.ExtractUnitCount(raw.Title);
            product.LastSeenAt = DateTimeOffset.UtcNow;
            product.Status = ProductStatus.Active;

            if (isNewProduct && classification.RequiresReview)
            {
                db.ReviewItems.Add(new ReviewItem
                {
                    Kind = ReviewItemKind.LowConfidenceClassification,
                    Reason = classification.ReviewReason ?? "Low confidence classification on coffee item.",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        competitorKey = competitor.Key,
                        productId = product.Id,
                        externalId = product.ExternalId,
                        title = decodedTitle,
                        category = classification.Category.ToString(),
                        confidence = classification.Confidence,
                        matchedRule = classification.MatchedRule
                    })
                });
            }

            Guid? firstVariantIdForNewProductEvent = null;

            var distinctVariants = raw.Variants
                .GroupBy(v => v.ExternalId)
                .Select(g => g.First())
                .ToList();

            foreach (var rawVariant in distinctVariants)
            {
                var isNewVariant = !variantLookup.TryGetValue((product.Id, rawVariant.ExternalId), out var existingVariantId);
                Variant variant;

                if (!isNewVariant && existingVariants.TryGetValue(existingVariantId, out var existingVar))
                {
                    variant = existingVar;
                }
                else
                {
                    variant = new Variant { ProductId = product.Id, ExternalId = rawVariant.ExternalId };
                    db.Variants.Add(variant);
                    variantLookup[(product.Id, rawVariant.ExternalId)] = variant.Id;
                    existingVariants[variant.Id] = variant;
                    isNewVariant = true;
                    variantsSaved++;
                }

                variant.Title = rawVariant.Title;
                variant.Sku = rawVariant.Sku;
                variant.WeightGrams = TitleNormalizer.ExtractWeightGrams(rawVariant.Title)
                                     ?? TitleNormalizer.ExtractWeightGrams(raw.Title);
                variant.UnitCount = TitleNormalizer.ExtractUnitCount(rawVariant.Title)
                                   ?? TitleNormalizer.ExtractUnitCount(raw.Title);

                firstVariantIdForNewProductEvent ??= variant.Id;
                pageVariantIdsToTouch.Add(variant.Id);

                latestObsLookup.TryGetValue(variant.Id, out var lastObservation);

                if (existingRunObs.TryGetValue(variant.Id, out var currentRunObs))
                {
                    // Already observed in this run - do not re-insert duplicate observation row (which violates unique (VariantId, RunId))
                    // If collector gave updated values, keep the current run observation up-to-date
                    currentRunObs.Price = rawVariant.Price;
                    currentRunObs.CompareAtPrice = rawVariant.CompareAtPrice;
                    currentRunObs.InStock = rawVariant.InStock;
                }
                else
                {
                    var observationChanged = lastObservation is null
                        || lastObservation.Price != rawVariant.Price
                        || lastObservation.CompareAtPrice != rawVariant.CompareAtPrice
                        || lastObservation.InStock != rawVariant.InStock;

                    if (observationChanged)
                    {
                        var newObs = new PriceObservation
                        {
                            VariantId = variant.Id,
                            RunId = runId,
                            Price = rawVariant.Price,
                            CompareAtPrice = rawVariant.CompareAtPrice,
                            Currency = rawVariant.Currency,
                            InStock = rawVariant.InStock,
                            ObservedAt = DateTimeOffset.UtcNow
                        };
                        db.PriceObservations.Add(newObs);
                        existingRunObs[variant.Id] = newObs;
                        observationsRecorded++;
                    }
                }

                if (rawVariant.CompareAtPrice.HasValue && rawVariant.CompareAtPrice.Value > rawVariant.Price)
                {
                    if (!existingOfferProductIds.Contains(product.Id))
                    {
                        var discountAmount = rawVariant.CompareAtPrice.Value - rawVariant.Price;
                        var discountPct = Math.Round((discountAmount / rawVariant.CompareAtPrice.Value) * 100m, 1);
                        var details = new
                        {
                            variantId = variant.Id,
                            price = rawVariant.Price,
                            compareAtPrice = rawVariant.CompareAtPrice.Value,
                            discountAmount,
                            discountPercentage = discountPct,
                            currency = rawVariant.Currency
                        };

                        db.Offers.Add(new Offer
                        {
                            CompetitorId = competitor.Id,
                            RunId = runId,
                            ProductId = product.Id,
                            Type = OfferType.Discount,
                            DetailsJson = JsonSerializer.Serialize(details)
                        });
                        existingOfferProductIds.Add(product.Id);
                    }
                }

                if (isNewProduct && firstVariantIdForNewProductEvent.HasValue)
                {
                    db.ChangeEvents.Add(new ChangeEvent
                    {
                        RunId = runId,
                        VariantId = firstVariantIdForNewProductEvent.Value,
                        Type = ChangeEventType.ProductAdded,
                        NewValue = decodedTitle
                    });
                    changeEventsRecorded++;
                }
                else if (!isNewVariant && lastObservation is not null)
                {
                    changeEventsRecorded += EmitChangeEventsForPriceUpdate(db, runId, variant.Id, lastObservation, rawVariant);
                }
            }
        }

        if (pageVariantIdsToTouch.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var touchIds = pageVariantIdsToTouch.ToArray();
            await db.Variants
                .Where(v => touchIds.Contains(v.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.LastObservedAt, now)
                    .SetProperty(v => v.LastSeenRunId, runId), ct);
        }

        competitorRun.Pages = Math.Max(competitorRun.Pages, pageNumber);
        competitorRun.Products = await db.Products.CountAsync(p => p.CompetitorId == competitor.Id && p.Status == ProductStatus.Active, ct);

        await db.SaveChangesAsync(ct);

        progressTracker?.UpdateCompetitorProgress(
            runId,
            competitor.Id,
            "Running",
            pageNumber,
            totalPages,
            competitorRun.Products,
            $"Ingested page {pageNumber} ({products.Count} products)");

        return new IngestBatchResult(productsSaved, variantsSaved, observationsRecorded, changeEventsRecorded);
    }

    public async Task<CompleteCompetitorResult> CompleteCompetitorAsync(
        Guid runId,
        string competitorSlug,
        string status,
        int totalPages,
        int totalProducts,
        string? error = null,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var competitor = await db.Competitors.FirstOrDefaultAsync(c => c.Key == competitorSlug, ct)
            ?? throw new InvalidOperationException($"Competitor with slug '{competitorSlug}' not found.");

        var competitorRun = await db.CompetitorRuns.FirstOrDefaultAsync(
            r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);

        if (competitorRun == null)
        {
            competitorRun = new CompetitorRun
            {
                RunId = runId,
                CompetitorId = competitor.Id
            };
            db.CompetitorRuns.Add(competitorRun);
        }

        competitorRun.Pages = Math.Max(competitorRun.Pages, totalPages);

        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            competitorRun.Status = CompetitorRunStatus.Failed;
            competitorRun.Coverage = RunCoverage.Partial;
            competitorRun.Errors = Math.Max(1, competitorRun.Errors + 1);
            var failMsg = error ?? "External collector reported failure.";
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Failed",
                totalPages,
                totalPages,
                competitorRun.Products,
                failMsg);

            await db.SaveChangesAsync(ct);
            return new CompleteCompetitorResult("Failed", competitorRun.Products, totalPages, failMsg);
        }

        // Check previous product count for Zero-products and 20% drop guards
        var previousCount = await db.CompetitorRuns
            .Where(cr => cr.CompetitorId == competitor.Id && cr.RunId != runId && cr.Status == CompetitorRunStatus.Completed)
            .OrderByDescending(cr => cr.CreatedAt)
            .Select(cr => cr.Products)
            .FirstOrDefaultAsync(ct);

        // Zero-products guard
        if (totalProducts == 0)
        {
            competitorRun.Status = CompetitorRunStatus.Failed;
            competitorRun.Coverage = RunCoverage.Partial;
            competitorRun.Products = 0;
            competitorRun.Errors = Math.Max(1, competitorRun.Errors + 1);
            var guardMsg = "Suspicious run: 0 products reported. Protected from product removals.";
            logger.LogWarning("Competitor {Slug} reported 0 products. Marking Failed to prevent removals.", competitorSlug);
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Failed",
                totalPages,
                totalPages,
                0,
                guardMsg);

            await db.SaveChangesAsync(ct);
            return new CompleteCompetitorResult("Failed", 0, totalPages, guardMsg);
        }

        // 20% drop guard
        if (previousCount > 0 && totalProducts < (previousCount * 0.2))
        {
            competitorRun.Status = CompetitorRunStatus.Failed;
            competitorRun.Coverage = RunCoverage.Partial;
            competitorRun.Products = totalProducts;
            competitorRun.Errors = Math.Max(1, competitorRun.Errors + 1);
            var dropMsg = $"Suspicious catalog drop: {totalProducts} items vs previous {previousCount} (< 20%). Protected from removals.";
            logger.LogWarning("Competitor {Slug} catalog dropped from {Prev} to {Count}. Marking Failed to prevent removals.", competitorSlug, previousCount, totalProducts);
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Suspicious",
                totalPages,
                totalPages,
                totalProducts,
                dropMsg);

            await db.SaveChangesAsync(ct);
            return new CompleteCompetitorResult("Failed", totalProducts, totalPages, dropMsg);
        }

        // Healthy completion: apply removal policy for unobserved products
        var candidates = await db.Products
            .Where(p => p.CompetitorId == competitor.Id && (p.Status == ProductStatus.Active || p.Status == ProductStatus.MissingOnce))
            .ToListAsync(ct);

        var seenExternalIds = await (from po in db.PriceObservations
                                     join v in db.Variants on po.VariantId equals v.Id
                                     join p in db.Products on v.ProductId equals p.Id
                                     where po.RunId == runId && p.CompetitorId == competitor.Id
                                     select p.ExternalId)
            .Distinct()
            .ToListAsync(ct);

        var seenSet = new HashSet<string>(seenExternalIds);

        foreach (var product in candidates.Where(p => !seenSet.Contains(p.ExternalId)))
        {
            var (newStatus, emitChangeEvent) = ProductRemovalConfirmation.EvaluateMissing(product.Status);
            product.Status = newStatus;

            if (emitChangeEvent)
            {
                var anyVariantId = await db.Variants
                    .Where(v => v.ProductId == product.Id)
                    .Select(v => v.Id)
                    .FirstOrDefaultAsync(ct);

                if (anyVariantId != Guid.Empty)
                {
                    db.ChangeEvents.Add(new ChangeEvent
                    {
                        RunId = runId,
                        VariantId = anyVariantId,
                        Type = ChangeEventType.ProductRemoved,
                        OldValue = product.Title
                    });
                }
            }
        }

        competitorRun.Status = CompetitorRunStatus.Completed;
        competitorRun.Coverage = RunCoverage.Complete;
        competitorRun.Products = totalProducts;

        await db.SaveChangesAsync(ct);

        var successMsg = $"Completed ({totalProducts} products across {totalPages} pages)";
        progressTracker?.UpdateCompetitorProgress(
            runId,
            competitor.Id,
            "Completed",
            totalPages,
            totalPages,
            totalProducts,
            successMsg);

        return new CompleteCompetitorResult("Completed", totalProducts, totalPages, successMsg);
    }

    private static int EmitChangeEventsForPriceUpdate(
        LazyBeanDbContext db, Guid runId, Guid variantId, PriceObservation previous, RawVariant current)
    {
        var count = 0;
        if (previous.Price != current.Price)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId,
                VariantId = variantId,
                Type = ChangeEventType.PriceChanged,
                OldValue = previous.Price.ToString("0.00"),
                NewValue = current.Price.ToString("0.00")
            });
            count++;
        }

        if (previous.InStock != current.InStock)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId,
                VariantId = variantId,
                Type = ChangeEventType.StockChanged,
                OldValue = previous.InStock?.ToString(),
                NewValue = current.InStock?.ToString()
            });
            count++;
        }

        var wasDiscounted = PriceLogic.IsDiscounted(previous.Price, previous.CompareAtPrice);
        var isDiscounted = PriceLogic.IsDiscounted(current.Price, current.CompareAtPrice);

        if (!wasDiscounted && isDiscounted)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId,
                VariantId = variantId,
                Type = ChangeEventType.DiscountStarted,
                NewValue = current.CompareAtPrice?.ToString("0.00")
            });
            count++;
        }
        else if (wasDiscounted && !isDiscounted)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId,
                VariantId = variantId,
                Type = ChangeEventType.DiscountEnded,
                OldValue = previous.CompareAtPrice?.ToString("0.00")
            });
            count++;
        }

        return count;
    }
}
