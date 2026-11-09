using System.Text.Json;
using System.Text.Json.Nodes;
using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MatchCandidate = LazyBean.Application.Matching.ProductMatcher.MatchCandidate;

namespace LazyBean.Application.Matching.AiMatching;

/// <summary>
/// Runs AI matching for one collection run. Rules remain the safety net:
/// <list type="bullet">
/// <item>Hard guardrails run first and can never be overridden by the AI.</item>
/// <item>A verdict is cached per pair hash, so only new or changed pairs reach the AI.</item>
/// <item>Confident "same" becomes an AI match. Unsure or mid-confidence goes to the review queue. "Different" is stored as rejected.</item>
/// <item>A manual decision (match or review) is never overridden.</item>
/// </list>
/// </summary>
public sealed class AiMatchingService(
    IUnitOfWork unitOfWork,
    IReadDbContext readContext,
    ProductMatcher matcher,
    IAiClient aiClient,
    ILogger<AiMatchingService> logger,
    IConfiguration? configuration = null,
    ICollectionProgressTracker? progressTracker = null) : IAiMatchingService
{
    private const string PhaseName = "ai_matching";
    private const string VerdictSame = "same";
    private const string VerdictDifferent = "different";
    private const string GuardrailProvider = "guardrail";
    private const string GuardrailModel = "rules";
    private const int MaxStoredReasonLength = 500;

    private static readonly MatchStatus[] ActiveStatuses = [MatchStatus.Auto, MatchStatus.Approved, MatchStatus.SizeVariant];

    private sealed record MatchInfo(Guid Id, MatchMethod Method, MatchStatus Status);

    private sealed record ReviewInfo(Guid Id, ReviewStatus Status, decimal Score, CanonicalCategory? Category, bool ResolvedByAi, bool AiSourcedMissed);

    private sealed class Comparison
    {
        public required MatchCandidate Competitor { get; init; }
        public required MatchCandidate Reference { get; init; }
        public required string Source { get; init; }
        public Guid? ReviewId { get; init; }
        public string Hash { get; set; } = string.Empty;
        public GuardrailVerdict Guard { get; set; } = new(false, null, false);

        public (Guid Competitor, Guid Reference) Key => (Competitor.VariantId, Reference.VariantId);
    }

    private sealed class RunState
    {
        public required AiMatchingOptions Options { get; init; }
        public required Guid? RunId { get; init; }
        public Dictionary<(Guid, Guid), MatchInfo> Matches { get; init; } = new();
        public Dictionary<(Guid, Guid), ReviewInfo> Reviews { get; init; } = new();
        public HashSet<string> CachedHashes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, AiMatchProviderUsage> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int UncertainCandidates;
        public int MissedCandidates;
        public int Evaluated;
        public int CacheHits;
        public int GuardrailBlocked;
        public int SentToAi;
        public int Confirmed;
        public int ToReview;
        public int Rejected;
        public int SkippedManual;
        public int SkippedQuota;
        public int Malformed;
        public string? StopReason;

        public bool AiAllowed => StopReason is null && SentToAi < Options.MaxComparisonsPerRun;

        public void AddUsage(string provider, int tokens)
        {
            var current = Providers.TryGetValue(provider, out var existing) ? existing : new AiMatchProviderUsage(0, 0);
            Providers[provider] = new AiMatchProviderUsage(current.Requests + 1, current.Tokens + Math.Max(0, tokens));
        }

        public AiMatchingStats ToStats() => new(
            NotRunReason: null,
            UncertainCandidates: UncertainCandidates,
            MissedCandidates: MissedCandidates,
            Evaluated: Evaluated,
            CacheHits: CacheHits,
            GuardrailBlocked: GuardrailBlocked,
            SentToAi: SentToAi,
            Confirmed: Confirmed,
            ToReview: ToReview,
            Rejected: Rejected,
            SkippedManual: SkippedManual,
            SkippedQuota: SkippedQuota,
            Malformed: Malformed,
            Providers: Providers.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            StopReason: StopReason);
    }

    public async Task<AiMatchingStats> RunAsync(Guid? runId, CancellationToken ct = default)
    {
        var options = AiMatchingOptions.From(configuration);
        if (!options.Enabled)
            return AiMatchingStats.NotRun("AI matching is disabled");

        var aiSettings = await readContext.Query<AiSetting>().FirstOrDefaultAsync(ct);
        if (aiSettings is null || !aiSettings.IsEnabled)
            return AiMatchingStats.NotRun("AI assistance is disabled");

        var referenceCompetitor = await readContext.Query<Competitor>().FirstOrDefaultAsync(c => c.IsReference, ct);
        if (referenceCompetitor is null)
            return AiMatchingStats.NotRun("no reference competitor");

        var dynamicBrands = await LoadDynamicBrandsAsync(ct);
        var state = new RunState
        {
            Options = options,
            RunId = runId,
            Matches = await LoadMatchIndexAsync(ct),
            Reviews = await LoadReviewIndexAsync(ct),
        };

        foreach (var category in Enum.GetValues<CanonicalCategory>())
        {
            ct.ThrowIfCancellationRequested();

            var references = await LoadCandidatesAsync(referenceCompetitor.Id, category, isReference: true, dynamicBrands, ct);
            var competitors = await LoadCandidatesAsync(referenceCompetitor.Id, category, isReference: false, dynamicBrands, ct);
            if (references.Count == 0 || competitors.Count == 0)
                continue;

            var comparisons = BuildComparisons(category, references, competitors, state);
            if (comparisons.Count > 0)
                await ProcessAsync(comparisons, state, ct);

            await unitOfWork.SaveChangesAsync(ct);
            unitOfWork.ClearTracker();
            Report(state);
        }

        logger.LogInformation("AI matching finished: {Summary}", state.ToStats().Summary);
        return state.ToStats();
    }

    // ---- Candidate selection -------------------------------------------------------------

    private List<Comparison> BuildComparisons(
        CanonicalCategory category,
        IReadOnlyList<MatchCandidate> references,
        IReadOnlyList<MatchCandidate> competitors,
        RunState state)
    {
        var referenceById = references.ToDictionary(r => r.VariantId);
        var competitorById = competitors.ToDictionary(c => c.VariantId);
        var result = new Dictionary<(Guid, Guid), Comparison>();

        // A. Uncertain rule matches: pending rule review items inside the configured score band.
        foreach (var (key, review) in state.Reviews)
        {
            if (review.Status != ReviewStatus.Pending || review.AiSourcedMissed || review.Category != category)
                continue;

            if (review.Score < state.Options.UncertainMinScore || review.Score > state.Options.UncertainMaxScore)
                continue;

            if (!competitorById.TryGetValue(key.Item1, out var competitor) || !referenceById.TryGetValue(key.Item2, out var reference))
                continue;

            result[key] = new Comparison { Competitor = competitor, Reference = reference, Source = "uncertain", ReviewId = review.Id };
        }

        // B. Missed matches: Lazy Bean products with no active match. For each competitor, the top N blocked candidates.
        var activeReferenceIds = state.Matches
            .Where(m => ActiveStatuses.Contains(m.Value.Status))
            .Select(m => m.Key.Item2)
            .ToHashSet();

        var competitorGroups = competitors.GroupBy(c => c.CompetitorKey).ToList();
        foreach (var reference in references)
        {
            if (activeReferenceIds.Contains(reference.VariantId))
                continue;

            foreach (var group in competitorGroups)
            {
                foreach (var candidate in AiMatchCandidateSelector.TopCandidates(reference, group, state.Options.MaxCandidatesPerCompetitor))
                {
                    var key = (candidate.VariantId, reference.VariantId);
                    if (state.Matches.ContainsKey(key) || state.Reviews.ContainsKey(key) || result.ContainsKey(key))
                        continue;

                    result[key] = new Comparison { Competitor = candidate, Reference = reference, Source = "missed" };
                }
            }
        }

        state.UncertainCandidates += result.Values.Count(c => c.Source == "uncertain");
        state.MissedCandidates += result.Values.Count(c => c.Source == "missed");
        return result.Values.ToList();
    }

    // ---- Decisions -----------------------------------------------------------------------

    private async Task ProcessAsync(List<Comparison> comparisons, RunState state, CancellationToken ct)
    {
        var prepared = new List<Comparison>();
        foreach (var comparison in comparisons)
        {
            state.Evaluated++;

            if (IsManuallyDecided(comparison.Key, state))
            {
                state.SkippedManual++;
                continue;
            }

            comparison.Hash = AiPairHasher.Compute(comparison.Competitor, comparison.Reference);
            comparison.Guard = AiMatchGuardrails.Evaluate(comparison.Competitor, comparison.Reference);
            prepared.Add(comparison);
        }

        if (prepared.Count == 0)
            return;

        var hashes = prepared.Select(p => p.Hash).Distinct().ToList();
        var cached = await readContext.Query<AiMatchDecision>()
            .Where(d => hashes.Contains(d.PairHash))
            .OrderByDescending(d => d.DecidedAt)
            .ToListAsync(ct);
        var cachedByHash = cached
            .GroupBy(d => d.PairHash, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var needsAi = new List<Comparison>();
        foreach (var comparison in prepared)
        {
            var hit = cachedByHash.GetValueOrDefault(comparison.Hash);

            if (comparison.Guard.Blocked)
            {
                state.GuardrailBlocked++;
                await ApplyAsync(comparison, VerdictDifferent, 1m, comparison.Guard.Reason ?? "blocked by guardrails",
                    GuardrailProvider, GuardrailModel, comparison.Guard.Reason, state, ct, writeCache: hit is null);
                continue;
            }

            // A cached guardrail row is re-decided if the guardrail no longer blocks (for example a bundle price became known).
            if (hit is not null && hit.GuardrailReason is null)
            {
                state.CacheHits++;
                await ApplyAsync(comparison, hit.Verdict, hit.Confidence, hit.Reason, hit.Provider, hit.ModelName,
                    null, state, ct, writeCache: false);
                continue;
            }

            needsAi.Add(comparison);
        }

        for (var i = 0; i < needsAi.Count; i += state.Options.BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = needsAi.Skip(i).Take(state.Options.BatchSize).ToList();
            if (!state.AiAllowed || state.SentToAi + batch.Count > state.Options.MaxComparisonsPerRun)
            {
                state.StopReason ??= "AI pair budget reached";
                state.SkippedQuota += needsAi.Count - i;
                break;
            }

            var pairs = batch.Select((c, index) => new AiMatchPairInput(index + 1, c.Reference, c.Competitor)).ToList();
            var (system, user) = AiMatchPrompt.Build(pairs);
            var maxOutput = Math.Min(2048, 120 * batch.Count + 120);

            AiCompletionResult completion;
            try
            {
                completion = await aiClient.CompleteJsonAsync(new AiJsonRequest(system, user, maxOutput), ct);
            }
            catch (AiUnavailableException ex)
            {
                state.StopReason = ex.QuotaReached ? "AI quota reached" : "AI providers unavailable";
                state.SkippedQuota += needsAi.Count - i;
                logger.LogWarning("AI matching stopped: {Reason}.", ex.Message);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                state.StopReason = "AI matching error";
                state.SkippedQuota += needsAi.Count - i;
                logger.LogWarning(ex, "AI matching batch failed. The remaining pairs stay for the next run.");
                break;
            }

            state.AddUsage(completion.Provider, completion.TotalTokens);
            state.SentToAi += batch.Count;

            var judgements = AiMatchResponseParser.Parse(completion.Text, batch.Count);
            if (judgements is null)
            {
                state.Malformed += batch.Count;
                logger.LogWarning("AI matching reply was malformed for a batch of {Count} pairs. Nothing was stored; they are retried next run.", batch.Count);
                Report(state);
                continue;
            }

            for (var j = 0; j < batch.Count; j++)
            {
                if (!judgements.TryGetValue(j + 1, out var judgement))
                {
                    state.Malformed++;
                    continue;
                }

                await ApplyAsync(batch[j], judgement.Verdict, judgement.Confidence, judgement.Reason,
                    completion.Provider, completion.Model, null, state, ct, writeCache: true);
            }

            await unitOfWork.SaveChangesAsync(ct);
            Report(state);
        }
    }

    private async Task ApplyAsync(
        Comparison comparison,
        string verdict,
        decimal confidence,
        string reason,
        string provider,
        string model,
        string? guardrailReason,
        RunState state,
        CancellationToken ct,
        bool writeCache)
    {
        var key = comparison.Key;
        state.Matches.TryGetValue(key, out var existingMatch);
        state.Reviews.TryGetValue(key, out var review);
        var pendingReview = review is { Status: ReviewStatus.Pending } ? review : null;
        var confirmThreshold = state.Options.ConfirmThreshold;

        if (verdict == VerdictSame && confidence >= confirmThreshold)
        {
            if (existingMatch is null)
            {
                var match = new ProductMatch
                {
                    CompetitorVariantId = comparison.Competitor.VariantId,
                    ReferenceVariantId = comparison.Reference.VariantId,
                    Score = confidence,
                    Method = MatchMethod.AI,
                    Status = MatchStatus.Auto,
                    Reason = Truncate(reason, MaxStoredReasonLength),
                    IsBundle = comparison.Guard.IsBundle,
                };
                await unitOfWork.ProductMatches.AddAsync(match, ct);
                state.Matches[key] = new MatchInfo(match.Id, MatchMethod.AI, MatchStatus.Auto);
            }

            if (pendingReview is not null)
                await ResolveReviewAsync(comparison, pendingReview, ReviewStatus.Approved, verdict, confidence, reason, provider, model, state, ct);

            state.Confirmed++;
        }
        else if (verdict == VerdictDifferent)
        {
            if (pendingReview is not null)
                await ResolveReviewAsync(comparison, pendingReview, ReviewStatus.Rejected, verdict, confidence, reason, provider, model, state, ct);

            state.Rejected++;
        }
        else
        {
            // Unsure, or same below the confirm threshold: a human decides in the review queue.
            if (review is null)
            {
                var payload = BuildReviewPayload(comparison, confidence, verdict, reason, provider, model, "missed", guardrailOk: !comparison.Guard.Blocked);
                var item = new ReviewItem
                {
                    Kind = ReviewItemKind.ProductMatch,
                    Status = ReviewStatus.Pending,
                    Reason = Truncate(reason, MaxStoredReasonLength),
                    PayloadJson = payload.ToJsonString(),
                };
                await unitOfWork.ReviewItems.AddAsync(item, ct);
                state.Reviews[key] = new ReviewInfo(item.Id, ReviewStatus.Pending, confidence,
                    comparison.Competitor.CanonicalCategory, ResolvedByAi: false, AiSourcedMissed: true);
            }
            else if (review.Status == ReviewStatus.Pending || review.ResolvedByAi)
            {
                await ResolveReviewAsync(comparison, review, ReviewStatus.Pending, verdict, confidence, reason, provider, model, state, ct);
            }

            state.ToReview++;
        }

        if (writeCache && state.CachedHashes.Add(comparison.Hash))
        {
            await unitOfWork.AiMatchDecisions.AddAsync(new AiMatchDecision
            {
                PairHash = comparison.Hash,
                CompetitorVariantId = comparison.Competitor.VariantId,
                ReferenceVariantId = comparison.Reference.VariantId,
                Verdict = verdict,
                Confidence = confidence,
                Reason = Truncate(reason, MaxStoredReasonLength),
                GuardrailReason = guardrailReason is null ? null : Truncate(guardrailReason, 300),
                Provider = provider,
                ModelName = model,
                DecidedAt = DateTimeOffset.UtcNow,
            }, ct);
        }
    }

    /// <summary>Writes the AI verdict into a review item and sets its status. AI-resolved items are marked so a later AI pass may reopen them.</summary>
    private async Task ResolveReviewAsync(
        Comparison comparison,
        ReviewInfo review,
        ReviewStatus status,
        string verdict,
        decimal confidence,
        string reason,
        string provider,
        string model,
        RunState state,
        CancellationToken ct)
    {
        var item = await unitOfWork.ReviewItems.GetByIdAsync(review.Id, ct);
        if (item is null)
            return;

        var node = JsonNode.Parse(item.PayloadJson) as JsonObject ?? new JsonObject();
        AnnotatePayload(node, comparison, verdict, confidence, reason, provider, model, review.AiSourcedMissed ? "missed" : "uncertain");
        if (status != ReviewStatus.Pending)
            node["ResolvedBy"] = "ai";
        else
            node.Remove("ResolvedBy");

        item.PayloadJson = node.ToJsonString();
        item.Status = status;
        unitOfWork.ReviewItems.Update(item);

        state.Reviews[comparison.Key] = review with { Status = status, ResolvedByAi = status != ReviewStatus.Pending };
    }

    private static JsonObject BuildReviewPayload(
        Comparison c,
        decimal confidence,
        string verdict,
        string reason,
        string provider,
        string model,
        string source,
        bool guardrailOk)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            CompetitorVariantId = c.Competitor.VariantId,
            ReferenceVariantId = c.Reference.VariantId,
            CompetitorProductId = c.Competitor.ProductId,
            ReferenceProductId = c.Reference.ProductId,
            CompetitorExternalId = c.Competitor.ExternalId,
            ReferenceExternalId = c.Reference.ExternalId,
            CompetitorTitle = c.Competitor.Title,
            ReferenceTitle = c.Reference.Title,
            CompetitorVendor = c.Competitor.Vendor,
            ReferenceVendor = c.Reference.Vendor,
            CompetitorBrand = c.Competitor.Brand,
            ReferenceBrand = c.Reference.Brand,
            CompetitorPrice = c.Competitor.Price,
            ReferencePrice = c.Reference.Price,
            CompetitorCategory = c.Competitor.Category ?? c.Competitor.CanonicalCategory.ToString(),
            ReferenceCategory = c.Reference.Category ?? c.Reference.CanonicalCategory.ToString(),
            CanonicalCategory = c.Competitor.CanonicalCategory.ToString(),
            Score = confidence,
            CanonicalUrl = c.Competitor.CanonicalUrl,
            ReferenceUrl = c.Reference.CanonicalUrl,
            CompetitorImageUrl = c.Competitor.ImageUrl,
            ReferenceImageUrl = c.Reference.ImageUrl,
            CompetitorWeightGrams = c.Competitor.WeightGrams,
            ReferenceWeightGrams = c.Reference.WeightGrams,
            CompetitorUnitCount = c.Competitor.UnitCount,
            ReferenceUnitCount = c.Reference.UnitCount,
            CompetitorCapsuleSystem = c.Competitor.CapsuleSystem,
            ReferenceCapsuleSystem = c.Reference.CapsuleSystem,
            CompetitorBeverageType = c.Competitor.BeverageType,
            ReferenceBeverageType = c.Reference.BeverageType,
            IsBundle = c.Guard.IsBundle,
        }))!.AsObject();

        AnnotatePayload(node, c, verdict, confidence, reason, provider, model, source);
        node["GuardrailOk"] = guardrailOk;
        return node;
    }

    private static void AnnotatePayload(
        JsonObject node,
        Comparison c,
        string verdict,
        decimal confidence,
        string reason,
        string provider,
        string model,
        string source)
    {
        node["AiVerdict"] = verdict;
        node["AiConfidence"] = confidence;
        node["AiReason"] = Truncate(reason, MaxStoredReasonLength);
        node["AiProvider"] = provider;
        node["AiModel"] = model;
        node["AiPairHash"] = c.Hash;
        node["AiSource"] = source;

    }

    // ---- Manual protection ---------------------------------------------------------------

    private static bool IsManuallyDecided((Guid Competitor, Guid Reference) key, RunState state)
    {
        if (state.Matches.TryGetValue(key, out var match) && match.Method == MatchMethod.Manual)
            return true;

        return state.Reviews.TryGetValue(key, out var review) && review.Status != ReviewStatus.Pending && !review.ResolvedByAi;
    }

    // ---- Loading -------------------------------------------------------------------------

    private async Task<HashSet<string>> LoadDynamicBrandsAsync(CancellationToken ct)
    {
        var vendors = await readContext.Query<Product>()
            .Where(p => p.Vendor != null && p.Vendor != "")
            .Select(p => p.Vendor!)
            .Distinct()
            .ToListAsync(ct);

        var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vendor in vendors)
        {
            var clean = vendor.Trim().ToLowerInvariant();
            if (!ProductMatcher.StoreNames.Contains(clean) && clean.Length >= 3)
                brands.Add(clean);
        }

        return brands;
    }

    private async Task<Dictionary<(Guid, Guid), MatchInfo>> LoadMatchIndexAsync(CancellationToken ct)
    {
        var rows = await readContext.Query<ProductMatch>()
            .Select(m => new { m.Id, m.CompetitorVariantId, m.ReferenceVariantId, m.Method, m.Status })
            .ToListAsync(ct);

        var index = new Dictionary<(Guid, Guid), MatchInfo>();
        foreach (var row in rows)
            index.TryAdd((row.CompetitorVariantId, row.ReferenceVariantId), new MatchInfo(row.Id, row.Method, row.Status));

        return index;
    }

    private async Task<Dictionary<(Guid, Guid), ReviewInfo>> LoadReviewIndexAsync(CancellationToken ct)
    {
        var rows = await readContext.Query<ReviewItem>()
            .Where(r => r.Kind == ReviewItemKind.ProductMatch)
            .Select(r => new { r.Id, r.Status, r.PayloadJson })
            .ToListAsync(ct);

        var index = new Dictionary<(Guid, Guid), ReviewInfo>();
        foreach (var row in rows)
        {
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("CompetitorVariantId", out var comp) || !root.TryGetProperty("ReferenceVariantId", out var reference))
                    continue;

                var score = root.TryGetProperty("Score", out var scoreProp) && scoreProp.TryGetDecimal(out var s) ? s : 0m;
                var category = root.TryGetProperty("CanonicalCategory", out var catProp) && catProp.ValueKind == JsonValueKind.String
                    && Enum.TryParse<CanonicalCategory>(catProp.GetString(), out var parsed) ? parsed : (CanonicalCategory?)null;
                var resolvedByAi = root.TryGetProperty("ResolvedBy", out var resolvedProp) && resolvedProp.ValueKind == JsonValueKind.String
                    && resolvedProp.GetString() == "ai";
                var aiMissed = root.TryGetProperty("AiSource", out var sourceProp) && sourceProp.ValueKind == JsonValueKind.String
                    && sourceProp.GetString() == "missed";

                index.TryAdd((comp.GetGuid(), reference.GetGuid()),
                    new ReviewInfo(row.Id, row.Status, score, category, resolvedByAi, aiMissed));
            }
            catch (JsonException)
            {
                // Unreadable payloads are ignored, as in the rule matcher.
            }
        }

        return index;
    }

    private async Task<List<MatchCandidate>> LoadCandidatesAsync(
        Guid referenceCompetitorId,
        CanonicalCategory category,
        bool isReference,
        HashSet<string> dynamicBrands,
        CancellationToken ct)
    {
        var query =
            from p in readContext.Query<Product>()
            join v in readContext.Query<Variant>() on p.Id equals v.ProductId
            join c in readContext.Query<Competitor>() on p.CompetitorId equals c.Id
            where p.CanonicalCategory == category && p.Status == ProductStatus.Active
            select new
            {
                ProductId = p.Id,
                VariantId = v.Id,
                ExternalId = p.ExternalId,
                CompetitorId = p.CompetitorId,
                CompetitorKey = c.Key,
                Title = p.Title,
                NormalizedTitle = p.NormalizedTitle,
                Category = p.Category,
                ProductType = p.ProductType,
                Vendor = p.Vendor,
                Brand = p.Brand,
                CanonicalCategory = p.CanonicalCategory,
                WeightGrams = p.WeightGrams,
                UnitCount = p.UnitCount,
                Tags = p.Tags,
                Roast = p.Roast,
                CanonicalUrl = p.CanonicalUrl,
                ImageUrl = p.ImageUrl,
                Price = readContext.Query<PriceObservation>()
                    .Where(po => po.VariantId == v.Id)
                    .OrderByDescending(po => po.ObservedAt)
                    .Select(po => po.Price)
                    .FirstOrDefault(),
                IsReference = c.IsReference,
                Enabled = c.Enabled,
            };

        var filtered = isReference
            ? query.Where(x => x.CompetitorId == referenceCompetitorId)
            : query.Where(x => !x.IsReference && x.Enabled);

        var rows = await filtered.ToListAsync(ct);

        return rows.Select(r =>
        {
            var candidate = matcher.MapCandidate(
                r.ProductId, r.VariantId, r.ExternalId, r.CompetitorId, r.CompetitorKey,
                r.Title, r.NormalizedTitle, r.Category, r.ProductType, r.Vendor, r.Brand,
                r.CanonicalCategory, r.WeightGrams, r.UnitCount, r.Tags, r.Roast, r.Price, r.CanonicalUrl, dynamicBrands);
            candidate.ImageUrl = r.ImageUrl;
            return candidate;
        }).ToList();
    }

    // ---- Progress ------------------------------------------------------------------------

    private void Report(RunState state)
    {
        if (!state.RunId.HasValue || progressTracker is null)
            return;

        progressTracker.UpdatePhase(state.RunId.Value, PhaseName, "Running", state.ToStats().Summary);
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
