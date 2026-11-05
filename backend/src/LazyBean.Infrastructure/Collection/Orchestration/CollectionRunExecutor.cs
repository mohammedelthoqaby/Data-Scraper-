using System.Diagnostics;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Classification;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collection.Validation;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Orchestration;

// Orchestrates the run end-to-end: fetches raw items from each competitor's adapter,
// runs them through normalization, validation and price logic, and writes Products,
// Variants, PriceObservations, ChangeEvents and ReviewItems to the database.
//
// Concurrency note: each competitor task gets its own short-lived DbContext created
// from IDbContextFactory, because EF Core DbContext is not thread-safe.
public class CollectionRunExecutor(
    IDbContextFactory<LazyBeanDbContext> dbContextFactory,
    ICompetitorAdapterResolver adapters,
    ICacheService cache,
    IProductClassifier classifier,
    ICollectorHttpClient? http,
    IConfiguration? configuration,
    ILogger<CollectionRunExecutor> logger) : ICollectionRunExecutor
{
    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, new ProductClassifier(), null, null, logger)
    {
    }

    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        IProductClassifier classifier,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, classifier, null, null, logger)
    {
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken ct)
    {
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, ct);
            if (run.Status == CollectionRunStatus.Cancelled)
            {
                logger.LogInformation("CollectionRun {RunId} was already cancelled.", runId);
                return;
            }
            run.Status = CollectionRunStatus.Running;
            run.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        List<Competitor> competitors;
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, ct);
            if (!string.IsNullOrWhiteSpace(run.TargetCompetitorIdsJson))
            {
                var targetIds = JsonSerializer.Deserialize<List<Guid>>(run.TargetCompetitorIdsJson) ?? [];
                competitors = await db.Competitors.Where(c => c.Enabled && targetIds.Contains(c.Id)).ToListAsync(ct);
            }
            else
            {
                competitors = await db.Competitors.Where(c => c.Enabled).ToListAsync(ct);
            }
        }

        try
        {
            // Competitors run in parallel, each with its own DbContext; within each one,
            // FetchAsync (and therefore every HTTP request) is awaited sequentially.
            await Task.WhenAll(competitors.Select(competitor => ProcessCompetitorAsync(runId, competitor, ct)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("CollectionRun {RunId} execution was cancelled.", runId);
            await using (var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None))
            {
                var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
                run.Status = CollectionRunStatus.Cancelled;
                run.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            await cache.RemoveByTagAsync("runs", CancellationToken.None);
            return;
        }

        await using (var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None))
        {
            var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
            var compRuns = await db.CompetitorRuns.Where(cr => cr.RunId == runId).ToListAsync(CancellationToken.None);
            var anyDegradedOrFailed = compRuns.Any(cr => cr.Status == CompetitorRunStatus.Degraded || cr.Status == CompetitorRunStatus.Failed);
            var allFailed = compRuns.Count > 0 && compRuns.All(cr => cr.Status == CompetitorRunStatus.Failed);

            if (allFailed)
            {
                run.Status = CollectionRunStatus.Failed;
            }
            else if (anyDegradedOrFailed)
            {
                run.Status = CollectionRunStatus.Partial;
            }
            else
            {
                run.Status = CollectionRunStatus.Completed;
            }

            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await cache.RemoveByTagAsync("runs", CancellationToken.None);
        await cache.RemoveByTagAsync("products", CancellationToken.None);
    }

    private async Task ProcessCompetitorAsync(Guid runId, Competitor competitor, CancellationToken ct)
    {
        using var _ = CollectionRunContext.Begin(runId);
        var competitorStopwatch = Stopwatch.StartNew();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var competitorRun = new CompetitorRun
        {
            RunId = runId,
            CompetitorId = competitor.Id,
            Status = CompetitorRunStatus.Running
        };
        db.CompetitorRuns.Add(competitorRun);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // In-memory lookups for ExternalId -> Id to prevent O(N) database queries
        // and allow clearing the EF Core ChangeTracker per page without losing track of existing entities.
        var productLookup = await db.Products
            .Where(p => p.CompetitorId == competitor.Id)
            .ToDictionaryAsync(p => p.ExternalId, p => p.Id, ct);

        var variantLookup = await (from v in db.Variants
                                   join p in db.Products on v.ProductId equals p.Id
                                   where p.CompetitorId == competitor.Id
                                   select new { v.ProductId, v.ExternalId, v.Id })
            .ToDictionaryAsync(v => (v.ProductId, v.ExternalId), v => v.Id, ct);

        var seenExternalIds = new HashSet<string>();
        var errors = 0;
        var lastPage = 0;
        var pageStopwatch = Stopwatch.StartNew();
        var pageBatch = new List<RawProduct>();

        async Task PersistPageBatchAsync(List<RawProduct> batch, int pageNumber)
        {
            if (batch.Count == 0) return;

            var saveSw = Stopwatch.StartNew();

            try
            {
                // 1. Batch load existing products on this page
                var existingProductIds = batch
                    .Where(b => productLookup.ContainsKey(b.ExternalId))
                    .Select(b => productLookup[b.ExternalId])
                    .Distinct()
                    .ToList();

                var existingProducts = existingProductIds.Count > 0
                    ? await db.Products.Where(p => existingProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
                    : new Dictionary<Guid, Product>();

                // 2. Batch load existing variants on this page
                var existingVariantIds = batch
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

                // 3. Batch load latest observations for existing variants
                var latestObsList = existingVariantIds.Count > 0
                    ? await db.PriceObservations
                        .Where(po => existingVariantIds.Contains(po.VariantId) && po.RunId != runId)
                        .GroupBy(po => po.VariantId)
                        .Select(g => g.OrderByDescending(po => po.ObservedAt).First())
                        .ToListAsync(ct)
                    : new List<PriceObservation>();

                var latestObsLookup = latestObsList.ToDictionary(po => po.VariantId);
                var pageVariantIdsToTouch = new HashSet<Guid>();

                // 4. Process each product and its variants in-memory
                foreach (var raw in batch)
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
                        isNewProduct = true;
                    }

                    product.Title = decodedTitle;
                    product.NormalizedTitle = TitleNormalizer.NormalizeForMatching(raw.Title);
                    product.CanonicalUrl = raw.CanonicalUrl;
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

                    if (classification.RequiresReview)
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
                        Variant variant;
                        var isNewVariant = !variantLookup.TryGetValue((product.Id, rawVariant.ExternalId), out var existingVariantId);

                        if (!isNewVariant && existingVariants.TryGetValue(existingVariantId, out var existingVar))
                        {
                            variant = existingVar;
                        }
                        else
                        {
                            variant = new Variant { ProductId = product.Id, ExternalId = rawVariant.ExternalId };
                            db.Variants.Add(variant);
                            variantLookup[(product.Id, rawVariant.ExternalId)] = variant.Id;
                            isNewVariant = true;
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

                        var gate = PriceObservationGate.Evaluate(rawVariant.Price, rawVariant.Currency, lastObservation?.Price);
                        if (gate.Decision == PriceObservationDecision.SendToReview)
                        {
                            db.ReviewItems.Add(new ReviewItem
                            {
                                Kind = ReviewItemKind.SuspiciousPriceObservation,
                                Reason = gate.Reason ?? "Price observation failed validation.",
                                PayloadJson = JsonSerializer.Serialize(new
                                {
                                    competitorKey = competitor.Key,
                                    productExternalId = product.ExternalId,
                                    variantExternalId = variant.ExternalId,
                                    price = rawVariant.Price,
                                    currency = rawVariant.Currency
                                })
                            });
                            continue;
                        }

                        var observationChanged = isNewVariant
                            || lastObservation is null
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
                            latestObsLookup[variant.Id] = newObs;
                        }

                        if (rawVariant.CompareAtPrice.HasValue && rawVariant.CompareAtPrice.Value > rawVariant.Price)
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
                        }

                        if (!isNewVariant && lastObservation is not null)
                        {
                            EmitChangeEventsForPriceUpdate(db, runId, variant.Id, lastObservation, rawVariant);
                        }
                    }

                    if (isNewProduct && firstVariantIdForNewProductEvent is { } newVariantId)
                    {
                        db.ChangeEvents.Add(new ChangeEvent
                        {
                            RunId = runId,
                            VariantId = newVariantId,
                            Type = ChangeEventType.ProductAdded,
                            NewValue = decodedTitle
                        });
                    }
                }

                // 5. Persist entities
                await db.SaveChangesAsync(ct);

                // 6. Update LastObservedAt/LastSeenRunId per page with ONE set-based statement (ExecuteUpdateAsync)
                var variantIdsArray = pageVariantIdsToTouch.ToArray();
                if (variantIdsArray.Length > 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    await db.Variants
                        .Where(v => variantIdsArray.Contains(v.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(v => v.LastObservedAt, now)
                            .SetProperty(v => v.LastSeenRunId, runId), ct);
                }

                db.ChangeTracker.Clear();
                saveSw.Stop();

                var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                var heapMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
                logger.LogInformation(
                    "[{Competitor}] Page {Page} persisted {Count} products in {SaveMs}ms (total page duration: {TotalMs}ms). Working set: {WorkingSet} MB, managed heap: {ManagedHeap} MB.",
                    competitor.Key, pageNumber, batch.Count, saveSw.ElapsedMilliseconds, pageStopwatch.ElapsedMilliseconds, wsMb, heapMb);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Failed to persist batch for page {Page} of {Competitor}.", pageNumber, competitor.Key);
            }
        }

        try
        {
            var adapter = adapters.Resolve(competitor.Platform);

            await foreach (var raw in adapter.FetchAsync(competitor, ct))
            {
                var currentPage = CollectionRunContext.CurrentPageCount;
                if (currentPage != lastPage && lastPage > 0)
                {
                    await PersistPageBatchAsync(pageBatch, lastPage);
                    pageBatch.Clear();

                    lastPage = currentPage;
                    pageStopwatch.Restart();
                }
                else if (lastPage == 0)
                {
                    lastPage = currentPage;
                    pageStopwatch.Restart();
                }

                pageBatch.Add(raw);
                seenExternalIds.Add(raw.ExternalId);
            }

            // Flush final page
            if (pageBatch.Count > 0)
            {
                await PersistPageBatchAsync(pageBatch, lastPage);
                pageBatch.Clear();
            }

            competitorRun.Pages = CollectionRunContext.CurrentPageCount;
            competitorRun.Products = seenExternalIds.Count;
            competitorRun.Errors = errors;

            var recentCounts = await db.CompetitorRuns
                .Where(cr => cr.CompetitorId == competitor.Id
                             && cr.RunId != runId
                             && cr.Status == CompetitorRunStatus.Completed)
                .OrderByDescending(cr => cr.CreatedAt)
                .Take(3)
                .Select(cr => cr.Products)
                .ToListAsync(ct);

            if (CollectionRunContext.IsDegraded)
            {
                competitorRun.Status = CompetitorRunStatus.Degraded;
                competitorRun.Coverage = RunCoverage.Partial;
                logger.LogWarning(
                    "{Competitor}: run marked Degraded because {Reason} - no removals applied.",
                    competitor.Key, CollectionRunContext.DegradedReason ?? "guard triggered");
            }
            else if (ProductRemovalPolicy.IsHealthy(seenExternalIds.Count, recentCounts))
            {
                competitorRun.Status = CompetitorRunStatus.Completed;
                competitorRun.Coverage = RunCoverage.Complete;
                await MarkMissingProductsRemovedAsync(db, runId, competitor.Id, seenExternalIds, ct);
            }
            else
            {
                competitorRun.Status = CompetitorRunStatus.Degraded;
                competitorRun.Coverage = RunCoverage.Partial;
                logger.LogWarning(
                    "{Competitor}: product count {Count} dropped sharply vs. recent runs - marked Degraded, no removals applied.",
                    competitor.Key, seenExternalIds.Count);
            }

            if (competitor.IsReference && http != null)
            {
                await CheckReferenceLinksSampleAsync(db, competitor, runId, competitorRun, ct);
            }

            var compWsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            var compHeapMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
            logger.LogInformation(
                "[{Competitor}] Competitor run completed in {DurationMs}ms ({Products} products, {Pages} pages). Peak working set: {WorkingSet} MB, managed heap: {ManagedHeap} MB.",
                competitor.Key, competitorStopwatch.ElapsedMilliseconds, seenExternalIds.Count, competitorRun.Pages, compWsMb, compHeapMb);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            competitorRun.Pages = CollectionRunContext.CurrentPageCount;
            competitorRun.Products = seenExternalIds.Count;
            competitorRun.Errors = errors;
            competitorRun.Status = CompetitorRunStatus.Cancelled;
            competitorRun.Coverage = RunCoverage.Partial;
            logger.LogWarning("CompetitorRun for {Competitor} cancelled.", competitor.Key);
            try
            {
                db.CompetitorRuns.Attach(competitorRun);
                db.Entry(competitorRun).State = EntityState.Modified;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save cancelled CompetitorRun status for {Competitor}.", competitor.Key);
            }
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            competitorRun.Pages = CollectionRunContext.CurrentPageCount;
            competitorRun.Products = seenExternalIds.Count;
            competitorRun.Errors = errors;
            competitorRun.Status = CompetitorRunStatus.Degraded;
            competitorRun.Coverage = RunCoverage.Partial;
            logger.LogWarning("CompetitorRun for {Competitor} timed out - marked Degraded.", competitor.Key);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            competitorRun.Status = CompetitorRunStatus.Failed;
            competitorRun.Coverage = RunCoverage.Partial;
            competitorRun.Errors = errors + 1;
            logger.LogError(ex, "CompetitorRun failed for {Competitor}.", competitor.Key);
        }

        try
        {
            db.CompetitorRuns.Attach(competitorRun);
            db.Entry(competitorRun).State = EntityState.Modified;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save final CompetitorRun status for {Competitor}.", competitor.Key);
        }
    }

    private static void EmitChangeEventsForPriceUpdate(
        LazyBeanDbContext db, Guid runId, Guid variantId, PriceObservation previous, RawVariant current)
    {
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
        }

        var wasDiscounted = PriceLogic.IsDiscounted(previous.Price, previous.CompareAtPrice);
        var isDiscounted = PriceLogic.IsDiscounted(current.Price, current.CompareAtPrice);

        if (!wasDiscounted && isDiscounted)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId, VariantId = variantId, Type = ChangeEventType.DiscountStarted,
                NewValue = current.CompareAtPrice?.ToString("0.00")
            });
        }
        else if (wasDiscounted && !isDiscounted)
        {
            db.ChangeEvents.Add(new ChangeEvent
            {
                RunId = runId, VariantId = variantId, Type = ChangeEventType.DiscountEnded,
                OldValue = previous.CompareAtPrice?.ToString("0.00")
            });
        }
    }

    private static async Task MarkMissingProductsRemovedAsync(
        LazyBeanDbContext db, Guid runId, Guid competitorId, HashSet<string> seenExternalIds, CancellationToken ct)
    {
        var candidates = await db.Products
            .Where(p => p.CompetitorId == competitorId && (p.Status == ProductStatus.Active || p.Status == ProductStatus.MissingOnce))
            .ToListAsync(ct);

        foreach (var product in candidates.Where(p => !seenExternalIds.Contains(p.ExternalId)))
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

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task CheckReferenceLinksSampleAsync(
        LazyBeanDbContext db, Competitor competitor, Guid runId, CompetitorRun competitorRun, CancellationToken ct)
    {
        if (http == null) return;

        var apiBase = competitor.BaseUrl.TrimEnd('/');
        if (!apiBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            apiBase += "/api";
        }

        var sampleSize = int.TryParse(configuration?["Collection:ReferenceLinkCheckSampleSize"], out var sz) && sz > 0 ? sz : 50;

        var sampleProducts = await db.Products
            .Where(p => p.CompetitorId == competitor.Id && p.Status == ProductStatus.Active && !string.IsNullOrWhiteSpace(p.ExternalId))
            .OrderBy(p => p.Id)
            .Take(sampleSize)
            .Select(p => new { p.Id, p.ExternalId, p.Title, p.CanonicalUrl })
            .ToListAsync(ct);

        if (sampleProducts.Count == 0) return;

        logger.LogInformation("[{Competitor}] Verifying active reference links on sample of {Count} products...", competitor.Key, sampleProducts.Count);

        var brokenLinks = new List<(Guid ProductId, string Title, string Url, string Reason)>();

        foreach (var p in sampleProducts)
        {
            if (ct.IsCancellationRequested) break;

            var checkUrl = $"{apiBase}/products/{p.ExternalId}?lang=en-US";
            try
            {
                var resp = await http.GetAsync(competitor, checkUrl, ct);
                if (!resp.Success || resp.StatusCode >= 400)
                {
                    brokenLinks.Add((p.Id, p.Title, p.CanonicalUrl, $"HTTP {resp.StatusCode}"));
                }
            }
            catch (Exception ex)
            {
                brokenLinks.Add((p.Id, p.Title, p.CanonicalUrl, ex.Message));
            }
        }

        if (brokenLinks.Count > 0)
        {
            logger.LogWarning("[{Competitor}] Reference link check flagged {BrokenCount}/{Total} broken links.", competitor.Key, brokenLinks.Count, sampleProducts.Count);
            competitorRun.Errors += brokenLinks.Count;

            foreach (var broken in brokenLinks)
            {
                db.ReviewItems.Add(new ReviewItem
                {
                    Kind = ReviewItemKind.BrokenReferenceLink,
                    Reason = $"Broken reference link: {broken.Reason}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        competitorKey = competitor.Key,
                        productId = broken.ProductId,
                        productTitle = broken.Title,
                        url = broken.Url,
                        reason = broken.Reason,
                        checkedAt = DateTimeOffset.UtcNow
                    })
                });
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        else
        {
            logger.LogInformation("[{Competitor}] Reference link check passed: {Count}/{Total} (100%) active and valid.", competitor.Key, sampleProducts.Count, sampleProducts.Count);
        }
    }
}
