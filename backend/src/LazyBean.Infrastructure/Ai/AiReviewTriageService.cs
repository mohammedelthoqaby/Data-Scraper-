using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using LazyBean.Application.Collection.Orchestration;

namespace LazyBean.Infrastructure.Ai;

public class AiReviewTriageService : IAiTriageService
{
    private readonly LazyBeanDbContext _dbContext;
    private readonly IAiClient _aiClient;
    private readonly ICollectionProgressTracker? _progressTracker;
    private readonly ILogger<AiReviewTriageService> _logger;

    public AiReviewTriageService(
        LazyBeanDbContext dbContext,
        IAiClient aiClient,
        ICollectionProgressTracker? progressTracker,
        ILogger<AiReviewTriageService> logger)
    {
        _dbContext = dbContext;
        _aiClient = aiClient;
        _progressTracker = progressTracker;
        _logger = logger;
    }

    public AiReviewTriageService(
        LazyBeanDbContext dbContext,
        IAiClient aiClient,
        ILogger<AiReviewTriageService> logger)
        : this(dbContext, aiClient, null, logger)
    {
    }

    public string ComputePairHash(
        string refTitle,
        string? refBrand,
        string? refCategory,
        int? refWeight,
        int? refUnit,
        string? refCapsule,
        string compTitle,
        string? compBrand,
        string? compCategory,
        int? compWeight,
        int? compUnit,
        string? compCapsule)
    {
        var raw = string.Join("|",
            refTitle.Trim().ToLowerInvariant(),
            (refBrand ?? "").Trim().ToLowerInvariant(),
            (refCategory ?? "").Trim().ToLowerInvariant(),
            refWeight?.ToString() ?? "",
            refUnit?.ToString() ?? "",
            (refCapsule ?? "").Trim().ToLowerInvariant(),
            compTitle.Trim().ToLowerInvariant(),
            (compBrand ?? "").Trim().ToLowerInvariant(),
            (compCategory ?? "").Trim().ToLowerInvariant(),
            compWeight?.ToString() ?? "",
            compUnit?.ToString() ?? "",
            (compCapsule ?? "").Trim().ToLowerInvariant());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }

    public async Task<AiTriageStats> TriagePendingReviewsAsync(int? limit = null, CancellationToken ct = default, Guid? runId = null)
    {
        var setting = await _dbContext.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (setting == null || !setting.IsEnabled)
        {
            _logger.LogInformation("AI is disabled; skipping AI review triage.");
            return new AiTriageStats(0, 0, 0, 0, 0, 0);
        }

        // The per-run cap is configurable (default 200). A caller can ask for less, never for more.
        // Daily quotas are enforced by the AI router; an exhausted quota stops this run gracefully.
        var runCap = Math.Clamp(setting.TriageRunCap, 1, 1000);
        var effectiveLimit = Math.Clamp(limit ?? runCap, 1, runCap);

        var pendingItems = await _dbContext.ReviewItems
            .Where(r => r.Kind == ReviewItemKind.ProductMatch && r.Status == ReviewStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        if (pendingItems.Count == 0)
            return new AiTriageStats(0, 0, 0, 0, 0, 0);

        var total = pendingItems.Count;
        var cacheHits = 0;
        var apiCalls = 0;
        var sameCount = 0;
        var diffCount = 0;
        var unsureCount = 0;
        var deferred = 0;
        string? stopReason = null;

        // Parse items and hashes
        var requests = new List<(ReviewItem Item, AiReviewTriagePairRequest Request, JsonObject RootNode)>();

        foreach (var item in pendingItems)
        {
            try
            {
                var node = JsonNode.Parse(item.PayloadJson)?.AsObject();
                if (node == null) continue;

                // Pairs already decided by AI matching are left alone here so the two AI passes never overwrite each other.
                if (node["AiSource"] is not null) continue;

                var refTitle = node["ReferenceTitle"]?.GetValue<string>() ?? "";
                var refBrand = node["ReferenceBrand"]?.GetValue<string>() ?? node["ReferenceVendor"]?.GetValue<string>();
                var refCat = node["ReferenceCategory"]?.GetValue<string>() ?? node["CanonicalCategory"]?.GetValue<string>();
                var refWeight = node["ReferenceWeightGrams"]?.GetValue<int?>();
                var refUnit = node["ReferenceUnitCount"]?.GetValue<int?>();
                var refCapsule = node["ReferenceCapsuleSystem"]?.GetValue<string>();
                var refFormat = node["ReferenceBeverageType"]?.GetValue<string>() ?? "Coffee";

                var compTitle = node["CompetitorTitle"]?.GetValue<string>() ?? "";
                var compBrand = node["CompetitorBrand"]?.GetValue<string>() ?? node["CompetitorVendor"]?.GetValue<string>();
                var compCat = node["CompetitorCategory"]?.GetValue<string>();
                var compWeight = node["CompetitorWeightGrams"]?.GetValue<int?>();
                var compUnit = node["CompetitorUnitCount"]?.GetValue<int?>();
                var compCapsule = node["CompetitorCapsuleSystem"]?.GetValue<string>();
                var compFormat = node["CompetitorBeverageType"]?.GetValue<string>() ?? "Coffee";

                var hash = ComputePairHash(
                    refTitle, refBrand, refCat, refWeight, refUnit, refCapsule,
                    compTitle, compBrand, compCat, compWeight, compUnit, compCapsule);

                var req = new AiReviewTriagePairRequest(
                    item.Id,
                    refTitle, refBrand, refCat, refWeight, refUnit, refCapsule, refFormat,
                    compTitle, compBrand, compCat, compWeight, compUnit, compCapsule, compFormat,
                    hash);

                requests.Add((item, req, node));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse review item {Id} for AI triage: {Msg}", item.Id, ex.Message);
            }
        }

        // Check cache for all hashes
        var allHashes = requests.Select(r => r.Request.PairHash).Distinct().ToList();
        var cachedEntries = await _dbContext.AiReviewTriageCaches
            .Where(c => allHashes.Contains(c.PairHash))
            .ToDictionaryAsync(c => c.PairHash, ct);

        var uncached = new List<(ReviewItem Item, AiReviewTriagePairRequest Request, JsonObject RootNode)>();

        foreach (var tuple in requests)
        {
            if (cachedEntries.TryGetValue(tuple.Request.PairHash, out var cached))
            {
                cacheHits++;
                ApplyAiVerdictToPayload(tuple.RootNode, cached.Verdict, cached.Confidence, cached.Reason, cached.PairHash);
                tuple.Item.PayloadJson = tuple.RootNode.ToJsonString();

                if (cached.Verdict == "same") sameCount++;
                else if (cached.Verdict == "different") diffCount++;
                else unsureCount++;
            }
            else
            {
                uncached.Add(tuple);
            }
        }

        // Batch pairs per request (up to 10 pairs per prompt call)
        var batchSize = 10;
        var totalBatches = (uncached.Count + batchSize - 1) / Math.Max(1, batchSize);

        for (var i = 0; i < uncached.Count; i += batchSize)
        {
            var batchIndex = (i / batchSize) + 1;
            if (runId.HasValue)
            {
                _progressTracker?.UpdatePhase(runId.Value, "ai_triage", "Running", $"AI {batchIndex}/{totalBatches}");
            }

            var batchTuples = uncached.Skip(i).Take(batchSize).ToList();
            var batchRequests = batchTuples.Select(b => b.Request).ToList();

            apiCalls++;
            IReadOnlyList<AiReviewTriageResult> results;
            try
            {
                results = await _aiClient.TriageReviewBatchAsync(batchRequests, ct);
            }
            catch (AiUnavailableException ex)
            {
                // Quota or providers exhausted: stop AI work for this run. Unprocessed items stay pending.
                _logger.LogWarning("AI triage stopped at batch {BatchIndex}/{TotalBatches}: {Reason}.", batchIndex, totalBatches, ex.Message);
                stopReason = ex.QuotaReached ? "AI quota reached" : ex.Message;
                deferred = uncached.Count - i;
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // AI must never fail the run: keep what was evaluated and leave the rest pending.
                _logger.LogWarning(ex, "AI triage batch {BatchIndex}/{TotalBatches} failed; remaining items stay pending.", batchIndex, totalBatches);
                stopReason = "AI triage error";
                deferred = uncached.Count - i;
                break;
            }

            foreach (var res in results)
            {
                var match = batchTuples.FirstOrDefault(b => b.Request.ReviewId == res.ReviewId);
                if (match == default) continue;

                ApplyAiVerdictToPayload(match.RootNode, res.Verdict, res.Confidence, res.Reason, res.PairHash);
                match.Item.PayloadJson = match.RootNode.ToJsonString();

                // Store in cache table
                var cacheEntry = new AiReviewTriageCache
                {
                    PairHash = res.PairHash,
                    Verdict = res.Verdict,
                    Confidence = res.Confidence,
                    Reason = res.Reason,
                    ModelName = res.Model ?? "unknown",
                    AnalyzedAt = DateTimeOffset.UtcNow
                };
                _dbContext.AiReviewTriageCaches.Add(cacheEntry);

                if (res.Verdict == "same") sameCount++;
                else if (res.Verdict == "different") diffCount++;
                else unsureCount++;
            }
        }

        // Apply matching automation for all evaluated items
        await ApplyMatchingAutomationAsync(requests, ct);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AI Triage completed: {Total} processed, {Hits} cache hits, {Calls} API calls, {Deferred} deferred. (Same: {Same}, Diff: {Diff}, Unsure: {Unsure})",
            total, cacheHits, apiCalls, deferred, sameCount, diffCount, unsureCount);

        return new AiTriageStats(total, cacheHits, apiCalls, sameCount, diffCount, unsureCount, deferred, stopReason);
    }

    private async Task ApplyMatchingAutomationAsync(
        List<(ReviewItem Item, AiReviewTriagePairRequest Request, JsonObject RootNode)> requests,
        CancellationToken ct)
    {
        var variantPairs = new List<(Guid RefVarId, Guid CompVarId, ReviewItem Item, string Verdict, decimal Conf, AiReviewTriagePairRequest Req)>();

        foreach (var r in requests)
        {
            var vStr = r.RootNode["AiVerdict"]?.GetValue<string>();
            var cDec = r.RootNode["AiConfidence"]?.GetValue<decimal>() ?? 0m;
            if (string.IsNullOrEmpty(vStr)) continue;

            if (Guid.TryParse(r.RootNode["ReferenceVariantId"]?.GetValue<string>(), out var refVarId) &&
                Guid.TryParse(r.RootNode["CompetitorVariantId"]?.GetValue<string>(), out var compVarId))
            {
                variantPairs.Add((refVarId, compVarId, r.Item, vStr, cDec, r.Request));
            }
        }

        if (variantPairs.Count == 0) return;

        var refIds = variantPairs.Select(p => p.RefVarId).Distinct().ToList();
        var compIds = variantPairs.Select(p => p.CompVarId).Distinct().ToList();

        var existingMatches = await _dbContext.ProductMatches
            .Where(m => refIds.Contains(m.ReferenceVariantId) && compIds.Contains(m.CompetitorVariantId))
            .ToListAsync(ct);

        foreach (var pair in variantPairs)
        {
            var match = existingMatches.FirstOrDefault(m => m.ReferenceVariantId == pair.RefVarId && m.CompetitorVariantId == pair.CompVarId);

            if (pair.Verdict == "same" && pair.Conf >= 0.95m && RulesConfirmSameMatch(pair.Req))
            {
                // Auto-approve as AI method
                pair.Item.Status = ReviewStatus.Approved;
                pair.Item.UpdatedAt = DateTimeOffset.UtcNow;

                if (match != null)
                {
                    match.Status = MatchStatus.Approved;
                    match.Method = MatchMethod.AI;
                    match.Score = pair.Conf;
                    match.UpdatedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    var newMatch = new ProductMatch
                    {
                        ReferenceVariantId = pair.RefVarId,
                        CompetitorVariantId = pair.CompVarId,
                        Score = pair.Conf,
                        Method = MatchMethod.AI,
                        Status = MatchStatus.Approved,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _dbContext.ProductMatches.Add(newMatch);
                    existingMatches.Add(newMatch);
                }
            }
            else if (pair.Verdict == "different" && pair.Conf >= 0.90m)
            {
                // Auto-reject
                pair.Item.Status = ReviewStatus.Rejected;
                pair.Item.UpdatedAt = DateTimeOffset.UtcNow;

                if (match != null)
                {
                    match.Status = MatchStatus.Rejected;
                    match.Method = MatchMethod.AI;
                    match.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                // Leave pending for manual review
                pair.Item.Status = ReviewStatus.Pending;
                pair.Item.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public static bool RulesConfirmSameMatch(AiReviewTriagePairRequest req)
    {
        // 1. Brand confirm
        var b1 = (req.RefBrand ?? "").Trim().ToLowerInvariant();
        var b2 = (req.CompBrand ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(b1) && !string.IsNullOrEmpty(b2))
        {
            if (!b1.Contains(b2) && !b2.Contains(b1))
                return false;
        }

        // 2. Size confirm (<= 2%)
        if (req.RefWeightGrams.HasValue && req.CompWeightGrams.HasValue && req.RefWeightGrams.Value > 0)
        {
            var diff = Math.Abs(req.RefWeightGrams.Value - req.CompWeightGrams.Value);
            if ((decimal)diff / req.RefWeightGrams.Value > 0.02m)
                return false;
        }
        else if (req.RefWeightGrams.HasValue != req.CompWeightGrams.HasValue)
        {
            return false;
        }

        if (req.RefUnitCount.HasValue && req.CompUnitCount.HasValue && req.RefUnitCount.Value > 0)
        {
            if (req.RefUnitCount.Value != req.CompUnitCount.Value)
                return false;
        }

        // 3. Format / capsule system equal
        var c1 = (req.RefCapsuleSystem ?? "").Trim().ToLowerInvariant();
        var c2 = (req.CompCapsuleSystem ?? "").Trim().ToLowerInvariant();
        if (c1 != c2)
            return false;

        var f1 = (req.RefFormat ?? "").Trim().ToLowerInvariant();
        var f2 = (req.CompFormat ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(f1) && !string.IsNullOrEmpty(f2) && f1 != f2)
            return false;

        return true;
    }

    private static void ApplyAiVerdictToPayload(
        JsonObject node,
        string verdict,
        decimal confidence,
        string reason,
        string pairHash)
    {
        node["AiVerdict"] = verdict;
        node["AiConfidence"] = confidence;
        node["AiReason"] = reason;
        node["AiPairHash"] = pairHash;
        node["AiAnalyzedAt"] = DateTimeOffset.UtcNow.ToString("O");
    }
}
