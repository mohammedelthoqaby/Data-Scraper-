using LazyBean.Application.Matching.AiMatching;
using System.Diagnostics;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Classification;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collection.Validation;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Matching;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    IGoogleAdsSyncService? googleAdsSyncService,
    IServiceScopeFactory? serviceScopeFactory,
    ICollectionProgressTracker? progressTracker,
    IGitHubWorkflowDispatcher? gitHubWorkflowDispatcher,
    ILogger<CollectionRunExecutor> logger) : ICollectionRunExecutor
{
    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, new ProductClassifier(), null, null, null, null, null, null, logger)
    {
    }

    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        IProductClassifier classifier,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, classifier, null, null, null, null, null, null, logger)
    {
    }

    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        IProductClassifier classifier,
        ICollectorHttpClient? http,
        IConfiguration? configuration,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, classifier, http, configuration, null, null, null, null, logger)
    {
    }

    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        IProductClassifier classifier,
        ICollectorHttpClient? http,
        IConfiguration? configuration,
        IGoogleAdsSyncService? googleAdsSyncService,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, classifier, http, configuration, googleAdsSyncService, null, null, null, logger)
    {
    }

    public CollectionRunExecutor(
        IDbContextFactory<LazyBeanDbContext> dbContextFactory,
        ICompetitorAdapterResolver adapters,
        ICacheService cache,
        IProductClassifier classifier,
        ICollectorHttpClient? http,
        IConfiguration? configuration,
        IGoogleAdsSyncService? googleAdsSyncService,
        IServiceScopeFactory? serviceScopeFactory,
        ICollectionProgressTracker? progressTracker,
        ILogger<CollectionRunExecutor> logger)
        : this(dbContextFactory, adapters, cache, classifier, http, configuration, googleAdsSyncService, serviceScopeFactory, progressTracker, null, logger)
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
            if (run.StartedAt == null) run.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        List<Competitor> competitors;
        Dictionary<Guid, int> previousSizes = [];
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

            try
            {
                previousSizes = await db.CompetitorRuns
                    .Where(cr => cr.Status == CompetitorRunStatus.Completed)
                    .GroupBy(cr => cr.CompetitorId)
                    .Select(g => new { CompetitorId = g.Key, Products = g.OrderByDescending(x => x.CreatedAt).Select(x => x.Products).FirstOrDefault() })
                    .ToDictionaryAsync(x => x.CompetitorId, x => x.Products, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load previous competitor catalog sizes for progress weighting.");
            }
        }

        var completedCompetitorIds = new HashSet<Guid>();
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var existingCompRuns = await db.CompetitorRuns
                .Where(cr => cr.RunId == runId)
                .ToListAsync(ct);

            completedCompetitorIds = existingCompRuns
                .Where(cr => cr.Status == CompetitorRunStatus.Completed)
                .Select(cr => cr.CompetitorId)
                .ToHashSet();
        }

        progressTracker?.InitializeRun(runId, competitors, previousSizes);

        foreach (var cid in completedCompetitorIds)
        {
            progressTracker?.UpdateCompetitorProgress(
                runId,
                cid,
                "Completed",
                1,
                1,
                previousSizes.GetValueOrDefault(cid, 0),
                "Already completed in previous attempt (resumed)");
        }

        try
        {
            var serverCompetitors = competitors
                .Where(c => !completedCompetitorIds.Contains(c.Id) && !string.Equals(c.CollectionHost, "github", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var githubCompetitors = competitors
                .Where(c => !completedCompetitorIds.Contains(c.Id) && string.Equals(c.CollectionHost, "github", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (completedCompetitorIds.Count > 0)
            {
                logger.LogInformation("CollectionRun {RunId}: resuming with {PendingCount} pending competitors ({CompletedCount} already completed).",
                    runId, serverCompetitors.Count + githubCompetitors.Count, completedCompetitorIds.Count);
            }

            var serverTasks = serverCompetitors.Select(c => ProcessCompetitorAsync(runId, c, ct));
            var githubTasks = githubCompetitors.Select(c => WaitForGithubCompetitorAsync(runId, c, ct));

            await Task.WhenAll(serverTasks.Concat(githubTasks));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await using (var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None))
            {
                var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
                var isUserCancelled = run.Status == CollectionRunStatus.Cancelled;
                var targetStatus = isUserCancelled ? CollectionRunStatus.Cancelled : CollectionRunStatus.Interrupted;

                logger.LogWarning("CollectionRun {RunId} execution interrupted. Reason: {Reason}, TargetStatus: {Status}",
                    runId, isUserCancelled ? "User cancelled" : "Host shutdown / recycle", targetStatus);

                run.Status = targetStatus;
                run.FinishedAt = DateTimeOffset.UtcNow;

                if (targetStatus == CollectionRunStatus.Interrupted)
                {
                    var activeCompRuns = await db.CompetitorRuns
                        .Where(cr => cr.RunId == runId && cr.Status == CompetitorRunStatus.Running)
                        .ToListAsync(CancellationToken.None);

                    foreach (var cr in activeCompRuns)
                    {
                        cr.Status = CompetitorRunStatus.Interrupted;
                        cr.Errors = Math.Max(1, cr.Errors + 1);
                        cr.Coverage = RunCoverage.Partial;
                    }
                }

                await db.SaveChangesAsync(CancellationToken.None);
                progressTracker?.SetCompleted(runId, targetStatus.ToString());
            }
            await cache.RemoveByTagAsync("runs", CancellationToken.None);
            return;
        }

        CollectionRunStatus finalStatus;
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

            finalStatus = run.Status;
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await cache.RemoveByTagAsync("runs", CancellationToken.None);
        await cache.RemoveByTagAsync("products", CancellationToken.None);

        if (finalStatus != CollectionRunStatus.Failed && serviceScopeFactory != null)
        {
            await RunPostProcessingPipelineAsync(runId, ct);
        }

        progressTracker?.SetCompleted(runId, finalStatus.ToString());

        if (googleAdsSyncService != null)
        {
            try
            {
                await googleAdsSyncService.SyncGoogleAdsIfDueAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to run weekly Google Ads sync check after CollectionRun {RunId}.", runId);
            }
        }
    }

    private async Task RunPostProcessingPipelineAsync(Guid runId, CancellationToken ct)
    {
        var phases = new List<object>();
        int? totalMatches = null;

        using var scope = serviceScopeFactory!.CreateScope();
        var sp = scope.ServiceProvider;

        // Phase 1: Classification (10 min timeout)
        var swClass = Stopwatch.StartNew();
        progressTracker?.UpdatePhase(runId, "classification", "Running", "Ensuring active catalog is classified...");
        try
        {
            using var classCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            classCts.CancelAfter(TimeSpan.FromMinutes(10));

            var classifierService = sp.GetService<IProductClassifier>() ?? classifier;
            await using (var db = await dbContextFactory.CreateDbContextAsync(classCts.Token))
            {
                var unclassified = await db.Products
                    .Where(p => p.Status == ProductStatus.Active && (int)p.CanonicalCategory == 0)
                    .Take(1000)
                    .ToListAsync(classCts.Token);

                foreach (var prod in unclassified)
                {
                    var res = classifierService.Classify(prod.Title, prod.ProductType, prod.Vendor, prod.Tags, prod.Category);
                    prod.CanonicalCategory = res.Category;
                }

                if (unclassified.Count > 0)
                {
                    await db.SaveChangesAsync(classCts.Token);
                }

                swClass.Stop();
                var msg = $"{unclassified.Count} unclassified products processed";
                phases.Add(new { name = "classification", status = "completed", durationMs = (int)swClass.ElapsedMilliseconds, message = msg });
                progressTracker?.UpdatePhase(runId, "classification", "completed", msg, (int)swClass.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            swClass.Stop();
            var errMsg = "Phase timed out after 10 minutes.";
            logger.LogError(ex, "Post-collection classification phase timed out after 10 minutes.");
            phases.Add(new { name = "classification", status = "failed", durationMs = (int)swClass.ElapsedMilliseconds, message = errMsg });
            progressTracker?.UpdatePhase(runId, "classification", "failed", errMsg, (int)swClass.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            swClass.Stop();
            logger.LogError(ex, "Post-collection classification phase failed.");
            phases.Add(new { name = "classification", status = "failed", durationMs = (int)swClass.ElapsedMilliseconds, message = ex.Message });
            progressTracker?.UpdatePhase(runId, "classification", "failed", ex.Message, (int)swClass.ElapsedMilliseconds);
        }

        // Phase 2: Matching (10 min timeout)
        var swMatch = Stopwatch.StartNew();
        progressTracker?.UpdatePhase(runId, "matching", "Running", "Running strict category-by-category product matching...");
        try
        {
            using var matchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            matchCts.CancelAfter(TimeSpan.FromMinutes(10));

            var matcher = sp.GetRequiredService<IProductMatcher>();
            var matchResult = await matcher.MatchAllAsync(runId, matchCts.Token);
            swMatch.Stop();
            totalMatches = matchResult.AutoAcceptedCount;
            var msg = $"{matchResult.AutoAcceptedCount} auto-accepted, {matchResult.ReviewItemCount} reviews";
            phases.Add(new { name = "matching", status = "completed", durationMs = (int)swMatch.ElapsedMilliseconds, message = msg });
            progressTracker?.UpdatePhase(runId, "matching", "completed", msg, (int)swMatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            swMatch.Stop();
            var errMsg = "Phase timed out after 10 minutes.";
            logger.LogError(ex, "Post-collection matching phase timed out after 10 minutes.");
            phases.Add(new { name = "matching", status = "failed", durationMs = (int)swMatch.ElapsedMilliseconds, message = errMsg });
            progressTracker?.UpdatePhase(runId, "matching", "failed", errMsg, (int)swMatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            swMatch.Stop();
            logger.LogError(ex, "Post-collection matching phase failed.");
            phases.Add(new { name = "matching", status = "failed", durationMs = (int)swMatch.ElapsedMilliseconds, message = ex.Message });
            progressTracker?.UpdatePhase(runId, "matching", "failed", ex.Message, (int)swMatch.ElapsedMilliseconds);
        }

        // Phase 2b: AI product matching (10 min hard cap). Rules first; AI decides uncertain and missed pairs.
        var swAiMatch = Stopwatch.StartNew();
        progressTracker?.UpdatePhase(runId, "ai_matching", "Running", "AI matching: uncertain and missed pairs...");
        try
        {
            using var aiMatchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            aiMatchCts.CancelAfter(TimeSpan.FromMinutes(10));

            var aiMatching = sp.GetService<IAiMatchingService>();
            if (aiMatching != null)
            {
                var aiMatchStats = await aiMatching.RunAsync(runId, aiMatchCts.Token);
                swAiMatch.Stop();
                var aiMatchMsg = aiMatchStats.Summary;
                phases.Add(new { name = "ai_matching", status = "completed", durationMs = (int)swAiMatch.ElapsedMilliseconds, message = aiMatchMsg });
                progressTracker?.UpdatePhase(runId, "ai_matching", "completed", aiMatchMsg, (int)swAiMatch.ElapsedMilliseconds);
            }
            else
            {
                swAiMatch.Stop();
                phases.Add(new { name = "ai_matching", status = "skipped", durationMs = 0, message = "AI matching not registered" });
                progressTracker?.UpdatePhase(runId, "ai_matching", "skipped", "AI matching not registered", 0);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            swAiMatch.Stop();
            var errMsg = "timeout";
            logger.LogError(ex, "AI matching phase timed out after 10 minutes.");
            phases.Add(new { name = "ai_matching", status = "failed", durationMs = (int)swAiMatch.ElapsedMilliseconds, message = errMsg });
            progressTracker?.UpdatePhase(runId, "ai_matching", "failed", errMsg, (int)swAiMatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            swAiMatch.Stop();
            logger.LogError(ex, "AI matching phase failed. Rule matches are unaffected.");
            phases.Add(new { name = "ai_matching", status = "failed", durationMs = (int)swAiMatch.ElapsedMilliseconds, message = ex.Message });
            progressTracker?.UpdatePhase(runId, "ai_matching", "failed", ex.Message, (int)swAiMatch.ElapsedMilliseconds);
        }

        // Phase 3: AI Review Triage (5 min hard cap)
        var swAi = Stopwatch.StartNew();
        progressTracker?.UpdatePhase(runId, "ai_triage", "Running", "Evaluating pending review candidates with AI...");
        try
        {
            using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            aiCts.CancelAfter(TimeSpan.FromMinutes(5));

            var triageService = sp.GetService<IAiTriageService>();
            if (triageService != null)
            {
                var triageRes = await triageService.TriagePendingReviewsAsync(null, aiCts.Token, runId);
                swAi.Stop();
                var msg = triageRes.Deferred > 0
                    ? $"{(triageRes.StopReason == "AI quota reached" ? "AI quota reached" : $"AI stopped ({triageRes.StopReason})")}, {triageRes.Deferred} items left for next run ({triageRes.TotalEvaluated} evaluated)"
                    : $"{triageRes.TotalEvaluated} evaluated ({triageRes.SameCount} same, {triageRes.DifferentCount} different, {triageRes.UnsureCount} unsure)";
                phases.Add(new { name = "ai_triage", status = "completed", durationMs = (int)swAi.ElapsedMilliseconds, message = msg });
                progressTracker?.UpdatePhase(runId, "ai_triage", "completed", msg, (int)swAi.ElapsedMilliseconds);
            }
            else
            {
                swAi.Stop();
                phases.Add(new { name = "ai_triage", status = "skipped", durationMs = 0, message = "AI triage not registered" });
                progressTracker?.UpdatePhase(runId, "ai_triage", "skipped", "AI triage not registered", 0);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            swAi.Stop();
            var errMsg = "timeout";
            logger.LogError(ex, "Post-collection AI triage phase timed out after 5 minutes.");
            phases.Add(new { name = "ai_triage", status = "failed", durationMs = (int)swAi.ElapsedMilliseconds, message = errMsg });
            progressTracker?.UpdatePhase(runId, "ai_triage", "failed", errMsg, (int)swAi.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            swAi.Stop();
            var cleanErr = ex.Message.Contains("401") ? "401 invalid key"
                : ex.Message.Contains("429") ? "429 quota"
                : ex.Message.Contains("timeout") ? "timeout"
                : ex.Message;
            logger.LogWarning(ex, "Post-collection AI triage phase failed: {Message}", cleanErr);
            phases.Add(new { name = "ai_triage", status = "failed", durationMs = (int)swAi.ElapsedMilliseconds, message = cleanErr });
            progressTracker?.UpdatePhase(runId, "ai_triage", "failed", cleanErr, (int)swAi.ElapsedMilliseconds);
        }

        // Phase 4: Action Alerts Generation (10 min timeout)
        var swAlerts = Stopwatch.StartNew();
        progressTracker?.UpdatePhase(runId, "alerts", "Running", "Synchronizing manager action center alerts...");
        try
        {
            using var alertsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            alertsCts.CancelAfter(TimeSpan.FromMinutes(10));

            var alertService = sp.GetService<IAlertService>();
            if (alertService != null)
            {
                var alerts = await alertService.SyncAlertsAsync(alertsCts.Token);
                swAlerts.Stop();
                var msg = $"{alerts.Count} alerts generated";
                phases.Add(new { name = "alerts", status = "completed", durationMs = (int)swAlerts.ElapsedMilliseconds, message = msg });
                progressTracker?.UpdatePhase(runId, "alerts", "completed", msg, (int)swAlerts.ElapsedMilliseconds);
            }
            else
            {
                swAlerts.Stop();
                phases.Add(new { name = "alerts", status = "skipped", durationMs = 0, message = "AlertService not registered" });
                progressTracker?.UpdatePhase(runId, "alerts", "skipped", "AlertService not registered", 0);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            swAlerts.Stop();
            var errMsg = "Phase timed out after 10 minutes.";
            logger.LogError(ex, "Post-collection alerts sync phase timed out after 10 minutes.");
            phases.Add(new { name = "alerts", status = "failed", durationMs = (int)swAlerts.ElapsedMilliseconds, message = errMsg });
            progressTracker?.UpdatePhase(runId, "alerts", "failed", errMsg, (int)swAlerts.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            swAlerts.Stop();
            logger.LogError(ex, "Post-collection alerts sync phase failed.");
            phases.Add(new { name = "alerts", status = "failed", durationMs = (int)swAlerts.ElapsedMilliseconds, message = ex.Message });
            progressTracker?.UpdatePhase(runId, "alerts", "failed", ex.Message, (int)swAlerts.ElapsedMilliseconds);
        }

        // Save phase metadata and match counts onto CollectionRun
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            var run = await db.CollectionRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
            run.PhasesJson = JsonSerializer.Serialize(phases);
            if (totalMatches.HasValue)
            {
                run.MatchesCount = totalMatches.Value;
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist PhasesJson and MatchesCount to CollectionRun {RunId}.", runId);
        }
    }

    private async Task WaitForGithubCompetitorAsync(Guid runId, Competitor competitor, CancellationToken ct)
    {
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var cr = await db.CompetitorRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);
            if (cr == null)
            {
                cr = new CompetitorRun
                {
                    RunId = runId,
                    CompetitorId = competitor.Id,
                    Status = CompetitorRunStatus.Running
                };
                db.CompetitorRuns.Add(cr);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                cr.Status = CompetitorRunStatus.Running;
                cr.Errors = 0;
                await db.SaveChangesAsync(ct);
            }
        }

        progressTracker?.UpdateCompetitorProgress(
            runId,
            competitor.Id,
            "Running",
            0,
            null,
            0,
            "Dispatching GitHub Actions collector...");

        GitHubDispatchResult? dispatchResult = null;
        if (gitHubWorkflowDispatcher != null)
        {
            dispatchResult = await gitHubWorkflowDispatcher.DispatchCollectorAsync(runId, [competitor.Key], ct);
            if (!dispatchResult.Success)
            {
                logger.LogWarning("GitHub workflow dispatch failed for {Competitor}: {Error}", competitor.Key, dispatchResult.ErrorMessage);
                await using var db = await dbContextFactory.CreateDbContextAsync(ct);
                var cr = await db.CompetitorRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);
                if (cr != null)
                {
                    cr.Status = CompetitorRunStatus.Failed;
                    cr.Errors = Math.Max(1, cr.Errors + 1);
                    await db.SaveChangesAsync(ct);
                }

                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Degraded",
                    0,
                    null,
                    0,
                    dispatchResult.ErrorMessage);
            }
        }

        var start = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMinutes(30);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var cr = await db.CompetitorRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);
            if (cr != null && cr.Status != CompetitorRunStatus.Running && cr.Status != CompetitorRunStatus.Queued)
            {
                logger.LogInformation("External GitHub collector for {Competitor} finished with status {Status}.", competitor.Key, cr.Status);
                return;
            }

            var elapsed = DateTimeOffset.UtcNow - start;

            if (cr != null && cr.Products > 0)
            {
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Running",
                    cr.Pages,
                    null,
                    cr.Products,
                    $"Ingesting from GitHub collector ({cr.Products} items saved so far)");
            }
            else if (dispatchResult == null || dispatchResult.Success)
            {
                var elapsedFormatted = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Running",
                    0,
                    null,
                    0,
                    $"Waiting for GitHub collector ({elapsedFormatted} / 30:00)");
            }

            if (elapsed > timeout)
            {
                logger.LogWarning("GitHub Actions collector for {Competitor} timed out after 30 minutes. Marking Skipped.", competitor.Key);
                var entity = await db.CompetitorRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.CompetitorId == competitor.Id, ct);
                if (entity != null)
                {
                    entity.Status = CompetitorRunStatus.Failed;
                    entity.Coverage = RunCoverage.Partial;
                    entity.Errors = Math.Max(1, entity.Errors + 1);
                    await db.SaveChangesAsync(ct);
                }

                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Skipped",
                    cr?.Pages ?? 0,
                    null,
                    cr?.Products ?? 0,
                    "Skipped (collector did not report within 30m)");
                return;
            }
        }
    }

    private async Task ProcessCompetitorAsync(Guid runId, Competitor competitor, CancellationToken ct)
    {
        using var _ = CollectionRunContext.Begin(runId);
        var competitorStopwatch = Stopwatch.StartNew();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var competitorRun = await db.CompetitorRuns
            .FirstOrDefaultAsync(cr => cr.RunId == runId && cr.CompetitorId == competitor.Id, ct);

        if (competitorRun == null)
        {
            competitorRun = new CompetitorRun
            {
                RunId = runId,
                CompetitorId = competitor.Id,
                Status = CompetitorRunStatus.Running
            };
            db.CompetitorRuns.Add(competitorRun);
        }
        else
        {
            competitorRun.Status = CompetitorRunStatus.Running;
        }
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

                    progressTracker?.UpdateCompetitorProgress(
                        runId,
                        competitor.Id,
                        "Running",
                        currentPage,
                        CollectionRunContext.TotalPages,
                        seenExternalIds.Count,
                        CollectionRunContext.StatusMessage ?? $"Page {currentPage} saved");

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

            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Running",
                CollectionRunContext.CurrentPageCount,
                CollectionRunContext.TotalPages,
                seenExternalIds.Count,
                CollectionRunContext.StatusMessage ?? $"Processed {seenExternalIds.Count} items");

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

            var previousCount = recentCounts.FirstOrDefault();

            if (seenExternalIds.Count == 0)
            {
                var errorReason = CollectionRunContext.DegradedReason ?? "Zero products collected from source";
                competitorRun.Status = CompetitorRunStatus.Failed;
                competitorRun.Coverage = RunCoverage.Partial;
                competitorRun.Errors = errors + 1;
                logger.LogWarning(
                    "{Competitor}: 0 products collected. Marked Failed: {Reason} - no removals applied.",
                    competitor.Key, errorReason);
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Failed",
                    competitorRun.Pages,
                    CollectionRunContext.TotalPages,
                    0,
                    errorReason);
            }
            else if (previousCount > 0 && seenExternalIds.Count < 0.20 * previousCount)
            {
                var dropReason = $"Product count ({seenExternalIds.Count}) dropped below 20% of previous run ({previousCount}) - suspicious run";
                competitorRun.Status = CompetitorRunStatus.Failed;
                competitorRun.Coverage = RunCoverage.Partial;
                competitorRun.Errors = errors + 1;
                logger.LogWarning(
                    "{Competitor}: {Reason} - no removals applied.",
                    competitor.Key, dropReason);
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Suspicious",
                    competitorRun.Pages,
                    CollectionRunContext.TotalPages,
                    seenExternalIds.Count,
                    dropReason);
            }
            else if (CollectionRunContext.IsDegraded)
            {
                competitorRun.Status = CompetitorRunStatus.Degraded;
                competitorRun.Coverage = RunCoverage.Partial;
                logger.LogWarning(
                    "{Competitor}: run marked Degraded because {Reason} - no removals applied.",
                    competitor.Key, CollectionRunContext.DegradedReason ?? "guard triggered");
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Degraded",
                    competitorRun.Pages,
                    CollectionRunContext.TotalPages,
                    seenExternalIds.Count,
                    CollectionRunContext.DegradedReason ?? "guard triggered");
            }
            else if (ProductRemovalPolicy.IsHealthy(seenExternalIds.Count, recentCounts))
            {
                competitorRun.Status = CompetitorRunStatus.Completed;
                competitorRun.Coverage = RunCoverage.Complete;
                await MarkMissingProductsRemovedAsync(db, runId, competitor.Id, seenExternalIds, ct);
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    competitorRun.Status.ToString(),
                    competitorRun.Pages,
                    CollectionRunContext.TotalPages,
                    seenExternalIds.Count,
                    $"Finished with {seenExternalIds.Count} products");
            }
            else
            {
                competitorRun.Status = CompetitorRunStatus.Degraded;
                competitorRun.Coverage = RunCoverage.Partial;
                var reason = $"Product count {seenExternalIds.Count} dropped sharply vs. recent runs";
                logger.LogWarning(
                    "{Competitor}: {Reason} - marked Degraded, no removals applied.",
                    competitor.Key, reason);
                progressTracker?.UpdateCompetitorProgress(
                    runId,
                    competitor.Id,
                    "Degraded",
                    competitorRun.Pages,
                    CollectionRunContext.TotalPages,
                    seenExternalIds.Count,
                    reason);
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
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Cancelled",
                competitorRun.Pages,
                CollectionRunContext.TotalPages,
                seenExternalIds.Count,
                "Cancelled");
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
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Degraded",
                competitorRun.Pages,
                CollectionRunContext.TotalPages,
                seenExternalIds.Count,
                "Timed out");
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            competitorRun.Status = CompetitorRunStatus.Failed;
            competitorRun.Coverage = RunCoverage.Partial;
            competitorRun.Errors = errors + 1;
            logger.LogError(ex, "CompetitorRun failed for {Competitor}.", competitor.Key);
            progressTracker?.UpdateCompetitorProgress(
                runId,
                competitor.Id,
                "Failed",
                competitorRun.Pages,
                CollectionRunContext.TotalPages,
                seenExternalIds.Count,
                ex.Message);
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
