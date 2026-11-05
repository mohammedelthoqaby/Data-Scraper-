using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Matching;

public enum MatchDecision
{
    None = 0,
    Auto = 1,
    SizeVariant = 2,
    Review = 3
}

public sealed record MatchEvaluation(
    MatchDecision Decision,
    decimal Score,
    string Reason);

public class ProductMatcher : IProductMatcher
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadDbContext _readContext;
    private readonly ICacheService _cache;
    private readonly ILogger<ProductMatcher> _logger;
    private readonly HashSet<string> _genericTokens;

    public static readonly string[] BaseKnownBrands =
    [
        "lavazza", "illy", "davidoff", "nespresso", "starbucks", "kimbo", "melitta", "l'or", "lor",
        "bristot", "al ameed", "al-ameed", "karoma", "granell", "jadeite", "cafes novell", "novell",
        "caffitaly", "costa", "segafredo", "dallmayr", "pellini", "carraro", "cellini", "julius meinl",
        "tchibo", "nescafe", "jacobs", "trung nguyen", "torabika", "bonjorno", "abu auf", "shaheen",
        "misr cafe", "mokate", "bialetti", "hario", "timemore", "fellow", "delonghi", "krups", "breville",
        "ariete", "saeco", "philips", "flair", "wacaco", "gaggia", "eureka", "baratza", "comandante",
        "chemex", "aeropress", "bodum", "cafec", "origami", "kalita", "severin", "smeg", "jura", "moka",
        "vergnano", "ottoman", "mazago", "olis", "lamarzocco", "nuova simonelli", "rocket", "movenpick",
        "mövenpick", "monin", "torani", "1883", "davinci", "da vinci"
    ];

    private static readonly Dictionary<string, string> BrandAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["لافازا"] = "lavazza",
        ["لاڤازا"] = "lavazza",
        ["ايلي"] = "illy",
        ["إيلي"] = "illy",
        ["نسبريسو"] = "nespresso",
        ["نيسبريسو"] = "nespresso",
        ["ستاربكس"] = "starbucks",
        ["ستار بكس"] = "starbucks",
        ["ديفيدوف"] = "davidoff",
        ["دافيدوف"] = "davidoff",
        ["موفنبيك"] = "movenpick",
        ["موفينبيك"] = "movenpick",
        ["mövenpick"] = "movenpick",
        ["كاروما"] = "karoma",
        ["مونين"] = "monin",
        ["كيمبو"] = "kimbo",
        ["ميليتا"] = "melitta",
        ["لور"] = "lor",
        ["l'or"] = "lor",
        ["بريستوت"] = "bristot",
        ["العميد"] = "al ameed",
        ["al-ameed"] = "al ameed",
        ["أبو عوف"] = "abu auf",
        ["ابو عوف"] = "abu auf",
        ["شاهين"] = "shaheen",
        ["مصر كافيه"] = "misr cafe",
        ["بونجورنو"] = "bonjorno",
        ["جاكوبس"] = "jacobs",
        ["نسكافيه"] = "nescafe",
        ["كافيتالي"] = "caffitaly",
        ["caffitaly system"] = "caffitaly",
        ["سيجافريدو"] = "segafredo",
        ["دالماير"] = "dallmayr",
        ["فيرجنانو"] = "vergnano",
        ["فيرنانو"] = "vergnano",
        ["caffè vergnano"] = "vergnano",
        ["caffe vergnano"] = "vergnano",
        ["بياليتي"] = "bialetti",
        ["هاريو"] = "hario",
        ["تايمور"] = "timemore",
        ["فيلو"] = "fellow",
        ["ديلونجي"] = "delonghi",
        ["كروبس"] = "krups",
        ["بريفيل"] = "breville",
        ["كيمكس"] = "chemex",
        ["عثمان"] = "ottoman",
        ["مزاجو"] = "mazago",
        ["أوليس"] = "olis",
        ["اوليس"] = "olis"
    };

    private static readonly HashSet<string> StoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cafelax", "coffee zone", "coffee zone misr", "fengany", "coffee & cream",
        "coffee and cream", "strong coffee", "lazy bean", "lazzy bean", "general", "generic",
        "coffee station", "verona coffee", "verona"
    };

    private static readonly Regex CompatibilityRegex = new(
        @"(?:\bcompatible\s+(?:with|by|for)\s+[a-z0-9\-]+)|" +
        @"(?:[a-z0-9\-]+\s+compatible\b)|" +
        @"(?:\bcompatibility\s+(?:with|by|for)\s+[a-z0-9\-]+)|" +
        @"(?:\bby\s+(?:nespresso|dolce\s*gusto|caffitaly|vertuo)\b)|" +
        @"(?:\bfor\s+(?:nespresso|dolce\s*gusto|caffitaly|vertuo|original)\b)|" +
        @"(?:متوافق(?:ة|ه)?\s+(?:مع\s+)?(?:ماكينات|مكائن)?\s*[\u0600-\u06FFa-zA-Z0-9]+)|" +
        @"(?:لمكائن\s*[\u0600-\u06FFa-zA-Z0-9]+)|" +
        @"(?:لماكينات\s*[\u0600-\u06FFa-zA-Z0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NonAlphaNumericRegex = new(
        @"[^\p{L}\p{Nd}\s%]+", RegexOptions.Compiled);

    private static readonly Regex AccentsRegex = new(
        @"[àáâãäå]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ProductMatcher(
        IUnitOfWork unitOfWork,
        IReadDbContext readContext,
        ICacheService cache,
        ILogger<ProductMatcher> logger,
        IConfiguration? configuration = null)
    {
        _unitOfWork = unitOfWork;
        _readContext = readContext;
        _cache = cache;
        _logger = logger;
        _genericTokens = LoadGenericTokens(configuration);
    }

    public async Task<MatchingResult> MatchAllAsync(CancellationToken ct = default)
    {
        var wsStart = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        _logger.LogInformation("Starting strict product matching across all competitors against reference catalog. Initial working set: {Ws} MB.", wsStart);

        var referenceCompetitor = await _readContext.Query<Competitor>()
            .FirstOrDefaultAsync(c => c.IsReference, ct);

        if (referenceCompetitor is null)
        {
            _logger.LogWarning("No reference competitor found in database. Matching aborted.");
            return new MatchingResult(0, 0, new Dictionary<string, int>());
        }

        // Dynamically collect known brands from database Vendors (excluding store names)
        var dbVendors = await _readContext.Query<Product>()
            .Where(p => p.Vendor != null && p.Vendor != "")
            .Select(p => p.Vendor!)
            .Distinct()
            .ToListAsync(ct);

        var dynamicBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in dbVendors)
        {
            var clean = v.Trim().ToLowerInvariant();
            if (!StoreNames.Contains(clean) && clean.Length >= 3)
            {
                dynamicBrands.Add(clean);
            }
        }

        // Load reference products and variants
        var refQuery = await (
            from p in _readContext.Query<Product>()
            join v in _readContext.Query<Variant>() on p.Id equals v.ProductId
            where p.CompetitorId == referenceCompetitor.Id && p.Status == ProductStatus.Active
            select new
            {
                ProductId = p.Id,
                VariantId = v.Id,
                ExternalId = p.ExternalId,
                CompetitorId = p.CompetitorId,
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
                Price = _readContext.Query<PriceObservation>()
                    .Where(po => po.VariantId == v.Id)
                    .OrderByDescending(po => po.ObservedAt)
                    .Select(po => po.Price)
                    .FirstOrDefault(),
                CanonicalUrl = p.CanonicalUrl
            }).ToListAsync(ct);

        var refProducts = refQuery.Select(p =>
        {
            var weight = p.WeightGrams ?? TitleNormalizer.ExtractWeightGrams(p.Title);
            var unit = p.UnitCount ?? TitleNormalizer.ExtractUnitCount(p.Title, p.CanonicalCategory);
            var vol = TitleNormalizer.ExtractVolumeMl(p.Title);
            var brand = DetectBrand(p.Brand ?? p.Vendor, p.Title, dynamicBrands);
            var capsule = TitleNormalizer.ExtractCapsuleSystem(p.Title, p.CanonicalCategory);
            var beverage = TitleNormalizer.ExtractBeverageType(p.Title, p.CanonicalCategory);
            var distTokens = ExtractDistinguishingTokens(p.Title, brand, weight, unit, vol, p.CanonicalCategory, _genericTokens);

            return new MatchCandidate
            {
                ProductId = p.ProductId,
                VariantId = p.VariantId,
                ExternalId = p.ExternalId,
                CompetitorId = p.CompetitorId,
                CompetitorKey = referenceCompetitor.Key,
                Title = p.Title,
                NormalizedTitle = p.NormalizedTitle,
                Category = p.Category,
                ProductType = p.ProductType,
                Tags = p.Tags,
                Roast = p.Roast,
                Vendor = p.Vendor,
                CanonicalCategory = p.CanonicalCategory,
                WeightGrams = weight,
                UnitCount = unit,
                VolumeMl = vol,
                Price = p.Price,
                CanonicalUrl = p.CanonicalUrl,
                Tokens = TitleSimilarity.ExtractTokens(p.Title),
                Brand = brand,
                CapsuleSystem = capsule,
                BeverageType = beverage,
                DistinguishingTokens = distTokens
            };
        }).ToList();

        _logger.LogInformation("Loaded {Count} reference product variants for matching.", refProducts.Count);

        // Load all competitor products and variants
        var compQuery = await (
            from p in _readContext.Query<Product>()
            join v in _readContext.Query<Variant>() on p.Id equals v.ProductId
            join c in _readContext.Query<Competitor>() on p.CompetitorId equals c.Id
            where !c.IsReference && c.Enabled && p.Status == ProductStatus.Active
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
                Price = _readContext.Query<PriceObservation>()
                    .Where(po => po.VariantId == v.Id)
                    .OrderByDescending(po => po.ObservedAt)
                    .Select(po => po.Price)
                    .FirstOrDefault(),
                CanonicalUrl = p.CanonicalUrl
            }).ToListAsync(ct);

        var compProducts = compQuery.Select(p =>
        {
            var weight = p.WeightGrams ?? TitleNormalizer.ExtractWeightGrams(p.Title);
            var unit = p.UnitCount ?? TitleNormalizer.ExtractUnitCount(p.Title, p.CanonicalCategory);
            var vol = TitleNormalizer.ExtractVolumeMl(p.Title);
            var brand = DetectBrand(p.Brand ?? p.Vendor, p.Title, dynamicBrands);
            var capsule = TitleNormalizer.ExtractCapsuleSystem(p.Title, p.CanonicalCategory);
            var beverage = TitleNormalizer.ExtractBeverageType(p.Title, p.CanonicalCategory);
            var distTokens = ExtractDistinguishingTokens(p.Title, brand, weight, unit, vol, p.CanonicalCategory, _genericTokens);

            return new MatchCandidate
            {
                ProductId = p.ProductId,
                VariantId = p.VariantId,
                ExternalId = p.ExternalId,
                CompetitorId = p.CompetitorId,
                CompetitorKey = p.CompetitorKey,
                Title = p.Title,
                NormalizedTitle = p.NormalizedTitle,
                Category = p.Category,
                ProductType = p.ProductType,
                Tags = p.Tags,
                Roast = p.Roast,
                Vendor = p.Vendor,
                CanonicalCategory = p.CanonicalCategory,
                WeightGrams = weight,
                UnitCount = unit,
                VolumeMl = vol,
                Price = p.Price,
                CanonicalUrl = p.CanonicalUrl,
                Tokens = TitleSimilarity.ExtractTokens(p.Title),
                Brand = brand,
                CapsuleSystem = capsule,
                BeverageType = beverage,
                DistinguishingTokens = distTokens
            };
        }).ToList();

        _logger.LogInformation("Loaded {Count} competitor product variants for matching.", compProducts.Count);

        // Existing matches to avoid duplicate inserts
        var existingMatches = await _readContext.Query<ProductMatch>()
            .ToDictionaryAsync(m => (m.CompetitorVariantId, m.ReferenceVariantId), ct);

        // Existing review items for product matches
        var existingReviews = await _readContext.Query<ReviewItem>()
            .Where(r => r.Kind == ReviewItemKind.ProductMatch && r.Status == ReviewStatus.Pending)
            .ToListAsync(ct);

        var existingReviewKeys = new HashSet<(Guid CompVariantId, Guid RefVariantId)>();
        foreach (var rev in existingReviews)
        {
            try
            {
                using var doc = JsonDocument.Parse(rev.PayloadJson);
                if (doc.RootElement.TryGetProperty("CompetitorVariantId", out var cv) &&
                    doc.RootElement.TryGetProperty("ReferenceVariantId", out var rv))
                {
                    existingReviewKeys.Add((cv.GetGuid(), rv.GetGuid()));
                }
            }
            catch
            {
            }
        }

        var matchesPerCompetitor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sizeVariantsPerCompetitor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var autoAcceptedCount = 0;
        var sizeVariantCount = 0;
        var reviewItemCount = 0;

        // Group reference products by (CanonicalCategory, Brand)
        var refByCategoryAndBrand = refProducts
            .Where(p => p.Brand != null)
            .GroupBy(p => (p.CanonicalCategory, p.Brand!))
            .ToDictionary(g => g.Key, g => g.ToList());

        var refByCategory = refProducts
            .GroupBy(p => p.CanonicalCategory)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Process matching grouped by CanonicalCategory to maintain low working set memory
        foreach (var categoryGroup in compProducts.GroupBy(p => p.CanonicalCategory))
        {
            var category = categoryGroup.Key;

            foreach (var comp in categoryGroup)
        {
            List<MatchCandidate>? candidates = null;

            if (comp.Brand != null && refByCategoryAndBrand.TryGetValue((comp.CanonicalCategory, comp.Brand), out var brandMatches))
            {
                candidates = brandMatches;
            }
            else if (comp.Brand == null && refByCategory.TryGetValue(comp.CanonicalCategory, out var catMatches))
            {
                candidates = catMatches;
            }

            if (candidates is null || candidates.Count == 0)
                continue;

            MatchCandidate? bestCandidate = null;
            MatchEvaluation? bestEval = null;

            foreach (var reference in candidates)
            {
                var eval = EvaluateMatch(comp, reference, _genericTokens);
                if (eval.Decision == MatchDecision.None)
                    continue;

                if (bestEval is null || eval.Score > bestEval.Score ||
                    (eval.Decision == MatchDecision.Auto && bestEval.Decision != MatchDecision.Auto))
                {
                    bestEval = eval;
                    bestCandidate = reference;
                }
            }

            if (bestCandidate is null || bestEval is null)
                continue;

            var matchKey = (comp.VariantId, bestCandidate.VariantId);

            if (bestEval.Decision == MatchDecision.Auto)
            {
                if (existingMatches.TryGetValue(matchKey, out var match))
                {
                    match.Score = bestEval.Score;
                    match.Status = MatchStatus.Auto;
                }
                else
                {
                    var newMatch = new ProductMatch
                    {
                        CompetitorVariantId = comp.VariantId,
                        ReferenceVariantId = bestCandidate.VariantId,
                        Score = bestEval.Score,
                        Method = MatchMethod.Automatic,
                        Status = MatchStatus.Auto
                    };
                    await _unitOfWork.ProductMatches.AddAsync(newMatch, ct);
                    existingMatches[matchKey] = newMatch;
                }

                autoAcceptedCount++;
                matchesPerCompetitor[comp.CompetitorKey] = matchesPerCompetitor.GetValueOrDefault(comp.CompetitorKey) + 1;
            }
            else if (bestEval.Decision == MatchDecision.SizeVariant)
            {
                if (existingMatches.TryGetValue(matchKey, out var match))
                {
                    match.Score = bestEval.Score;
                    match.Status = MatchStatus.SizeVariant;
                }
                else
                {
                    var newMatch = new ProductMatch
                    {
                        CompetitorVariantId = comp.VariantId,
                        ReferenceVariantId = bestCandidate.VariantId,
                        Score = bestEval.Score,
                        Method = MatchMethod.Automatic,
                        Status = MatchStatus.SizeVariant
                    };
                    await _unitOfWork.ProductMatches.AddAsync(newMatch, ct);
                    existingMatches[matchKey] = newMatch;
                }

                sizeVariantCount++;
                sizeVariantsPerCompetitor[comp.CompetitorKey] = sizeVariantsPerCompetitor.GetValueOrDefault(comp.CompetitorKey) + 1;
            }
            else if (bestEval.Decision == MatchDecision.Review)
            {
                if (!existingReviewKeys.Contains(matchKey))
                {
                    var payload = new
                    {
                        CompetitorVariantId = comp.VariantId,
                        ReferenceVariantId = bestCandidate.VariantId,
                        CompetitorProductId = comp.ProductId,
                        ReferenceProductId = bestCandidate.ProductId,
                        CompetitorExternalId = comp.ExternalId,
                        ReferenceExternalId = bestCandidate.ExternalId,
                        CompetitorTitle = comp.Title,
                        ReferenceTitle = bestCandidate.Title,
                        CompetitorVendor = comp.Vendor,
                        ReferenceVendor = bestCandidate.Vendor,
                        CompetitorBrand = comp.Brand,
                        ReferenceBrand = bestCandidate.Brand,
                        CompetitorPrice = comp.Price,
                        ReferencePrice = bestCandidate.Price,
                        CompetitorCategory = comp.Category ?? comp.CanonicalCategory.ToString(),
                        ReferenceCategory = bestCandidate.Category ?? bestCandidate.CanonicalCategory.ToString(),
                        CanonicalCategory = comp.CanonicalCategory.ToString(),
                        Score = bestEval.Score,
                        CanonicalUrl = comp.CanonicalUrl,
                        ReferenceUrl = bestCandidate.CanonicalUrl,
                        CompetitorWeightGrams = comp.WeightGrams,
                        ReferenceWeightGrams = bestCandidate.WeightGrams,
                        CompetitorUnitCount = comp.UnitCount,
                        ReferenceUnitCount = bestCandidate.UnitCount,
                        CompetitorCapsuleSystem = comp.CapsuleSystem,
                        ReferenceCapsuleSystem = bestCandidate.CapsuleSystem,
                        CompetitorBeverageType = comp.BeverageType,
                        ReferenceBeverageType = bestCandidate.BeverageType
                    };

                    var review = new ReviewItem
                    {
                        Kind = ReviewItemKind.ProductMatch,
                        Status = ReviewStatus.Pending,
                        Reason = bestEval.Reason,
                        PayloadJson = JsonSerializer.Serialize(payload)
                    };

                    await _unitOfWork.ReviewItems.AddAsync(review, ct);
                    existingReviewKeys.Add(matchKey);
                    reviewItemCount++;
                }
            }

            var catWsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            _logger.LogInformation("Matching category {Category} processed. Current working set: {WorkingSet} MB.", category, catWsMb);
        }
    }

        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByTagAsync("products", ct);

        var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        var heapMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
        _logger.LogInformation(
            "Matching complete: {Accepted} auto-accepted, {SizeVariants} size variants, {Review} flagged for review. Working set: {WorkingSet} MB, managed heap: {ManagedHeap} MB.",
            autoAcceptedCount, sizeVariantCount, reviewItemCount, wsMb, heapMb);

        return new MatchingResult(autoAcceptedCount, reviewItemCount, matchesPerCompetitor, sizeVariantCount, sizeVariantsPerCompetitor);
    }

    public static MatchEvaluation EvaluateMatch(
        MatchCandidate comp,
        MatchCandidate reference,
        IReadOnlySet<string>? genericTokens = null)
    {
        // 1. Category conflict check
        if (comp.CanonicalCategory != reference.CanonicalCategory)
        {
            if (comp.CanonicalCategory != CanonicalCategory.Other && reference.CanonicalCategory != CanonicalCategory.Other)
            {
                return new MatchEvaluation(MatchDecision.None, 0m, "Category mismatch");
            }
        }

        // 2. Beverage type conflict check
        if (!string.Equals(comp.BeverageType, reference.BeverageType, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchEvaluation(MatchDecision.None, 0m, $"Beverage type mismatch: {comp.BeverageType} vs {reference.BeverageType}");
        }

        // 3. Capsule system conflict check
        if (comp.CapsuleSystem != null && reference.CapsuleSystem != null)
        {
            if (!string.Equals(comp.CapsuleSystem, reference.CapsuleSystem, StringComparison.OrdinalIgnoreCase))
            {
                return new MatchEvaluation(MatchDecision.None, 0m, $"Capsule system mismatch: {comp.CapsuleSystem} vs {reference.CapsuleSystem}");
            }
        }

        // 3b. Coffee physical format conflict check (Whole Beans vs Ground)
        var compFormat = ExtractCoffeeFormat(comp);
        var refFormat = ExtractCoffeeFormat(reference);
        if (compFormat != null && refFormat != null && !string.Equals(compFormat, refFormat, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchEvaluation(MatchDecision.None, 0m, $"Coffee format mismatch: {compFormat} vs {refFormat}");
        }

        // 4. Brand check
        var brandKnown = comp.Brand != null && reference.Brand != null;
        var brandMatches = brandKnown && string.Equals(comp.Brand, reference.Brand, StringComparison.OrdinalIgnoreCase);

        // If both have brands and they conflict -> immediately reject
        if (comp.Brand != null && reference.Brand != null && !brandMatches)
        {
            return new MatchEvaluation(MatchDecision.None, 0m, $"Brand conflict: {comp.Brand} vs {reference.Brand}");
        }

        // 5. Distinguishing tokens comparison
        var tokensMatch = comp.DistinguishingTokens.Count > 0 &&
                          reference.DistinguishingTokens.Count > 0 &&
                          comp.DistinguishingTokens.SetEquals(reference.DistinguishingTokens);

        // 6. Size comparison
        var compHasSize = comp.WeightGrams.HasValue || comp.VolumeMl.HasValue || comp.UnitCount.HasValue;
        var refHasSize = reference.WeightGrams.HasValue || reference.VolumeMl.HasValue || reference.UnitCount.HasValue;

        var weightMatches = false;
        var weightDiffers = false;
        if (comp.WeightGrams.HasValue && reference.WeightGrams.HasValue)
        {
            var maxWeight = Math.Max(comp.WeightGrams.Value, reference.WeightGrams.Value);
            var diff = Math.Abs(comp.WeightGrams.Value - reference.WeightGrams.Value);
            if (maxWeight > 0 && ((double)diff / maxWeight) <= 0.02)
            {
                weightMatches = true;
            }
            else
            {
                weightDiffers = true;
            }
        }

        var unitMatches = false;
        var unitDiffers = false;
        if (comp.UnitCount.HasValue && reference.UnitCount.HasValue)
        {
            if (comp.UnitCount.Value == reference.UnitCount.Value)
            {
                unitMatches = true;
            }
            else
            {
                unitDiffers = true;
            }
        }

        var volumeMatches = false;
        var volumeDiffers = false;
        if (comp.VolumeMl.HasValue && reference.VolumeMl.HasValue)
        {
            var maxVol = Math.Max(comp.VolumeMl.Value, reference.VolumeMl.Value);
            var diff = Math.Abs(comp.VolumeMl.Value - reference.VolumeMl.Value);
            if (maxVol > 0 && ((double)diff / maxVol) <= 0.02)
            {
                volumeMatches = true;
            }
            else
            {
                volumeDiffers = true;
            }
        }

        var sizesDiffer = weightDiffers || unitDiffers || volumeDiffers;
        var hasMatchingSize = weightMatches || unitMatches || volumeMatches;

        // Number discrepancy check (e.g. 4 cups vs 2 cups, or missing pack count)
        var compNums = ExtractSignificantNumbers(comp.Title, comp.WeightGrams, comp.VolumeMl, comp.UnitCount);
        var refNums = ExtractSignificantNumbers(reference.Title, reference.WeightGrams, reference.VolumeMl, reference.UnitCount);
        var numbersMatch = compNums.SetEquals(refNums);

        // Similarity metrics for fallback/review
        var dice = TitleSimilarity.TokenDiceSimilarity(comp.Tokens, reference.Tokens);
        var lev = TitleSimilarity.NormalizedLevenshtein(comp.NormalizedTitle, reference.NormalizedTitle);
        var baseScore = (0.70 * dice) + (0.30 * lev);

        // Decision logic:
        // A. AUTO-ACCEPT
        // - Brand is known on both sides and matches
        // - Same CanonicalCategory
        // - Distinguishing tokens match symmetrically
        // - Capsule system matches (if present)
        // - Beverage type matches
        // - Size is known on BOTH sides and matches exactly (within 2% tolerance)
        // - Standalone numbers match symmetrically
        if (brandMatches &&
            comp.CanonicalCategory == reference.CanonicalCategory &&
            tokensMatch &&
            numbersMatch &&
            compHasSize &&
            refHasSize &&
            hasMatchingSize &&
            !sizesDiffer)
        {
            var finalScore = comp.NormalizedTitle == reference.NormalizedTitle ? 1.0m : 0.98m;
            return new MatchEvaluation(MatchDecision.Auto, finalScore, "Exact brand, variant tokens and size match");
        }

        // B. SIZE-VARIANT MATCH
        // - Brand is known on both sides and matches
        // - Same CanonicalCategory
        // - Distinguishing tokens match symmetrically
        // - Size is known on BOTH sides
        // - Sizes differ (> 2%) or numbers differ (e.g. 4 cups vs 2 cups)
        if (brandMatches &&
            comp.CanonicalCategory == reference.CanonicalCategory &&
            tokensMatch &&
            compHasSize &&
            refHasSize &&
            (sizesDiffer || !numbersMatch))
        {
            return new MatchEvaluation(MatchDecision.SizeVariant, 0.92m,
                $"Size variant: '{comp.Title}' ({comp.WeightGrams}g/{comp.UnitCount}u/{comp.VolumeMl}ml) vs '{reference.Title}' ({reference.WeightGrams}g/{reference.UnitCount}u/{reference.VolumeMl}ml)");
        }

        // C. REVIEW ITEM
        // User requirement:
        // "keep a pair in review only if brand AND size AND CapsuleSystem/format match
        //  and only the variant tokens differ slightly. Everything else is 'NoMatch' (not stored as a review item)."
        var formatMatches = string.Equals(compFormat, refFormat, StringComparison.OrdinalIgnoreCase);
        var capsuleMatches = string.Equals(comp.CapsuleSystem, reference.CapsuleSystem, StringComparison.OrdinalIgnoreCase);
        var categoryMatches = comp.CanonicalCategory == reference.CanonicalCategory;
        var beverageMatches = string.Equals(comp.BeverageType, reference.BeverageType, StringComparison.OrdinalIgnoreCase);

        if (brandMatches &&
            compHasSize && refHasSize && hasMatchingSize && !sizesDiffer &&
            formatMatches &&
            capsuleMatches &&
            categoryMatches &&
            beverageMatches)
        {
            // Tightened rule: pairs whose distinguishing tokens are entirely different (e.g. Brasile vs Selection)
            // are NoMatch and must NOT enter the queue.
            var unsharedComp = comp.DistinguishingTokens
                .Where(t => !reference.DistinguishingTokens.Any(r => AreTokensSimilar(t, r)) && !IsSubstantiveModifier(t))
                .ToList();
            var unsharedRef = reference.DistinguishingTokens
                .Where(t => !comp.DistinguishingTokens.Any(c => AreTokensSimilar(c, t)) && !IsSubstantiveModifier(t))
                .ToList();

            if (unsharedComp.Count > 0 && unsharedRef.Count > 0)
            {
                return new MatchEvaluation(MatchDecision.None, 0m,
                    $"Conflicting variant tokens: '{string.Join(", ", unsharedComp)}' vs '{string.Join(", ", unsharedRef)}'");
            }

            var distDice = TitleSimilarity.TokenDiceSimilarity(comp.DistinguishingTokens, reference.DistinguishingTokens);
            var distOverlap = comp.DistinguishingTokens.Intersect(reference.DistinguishingTokens).Any();
            var similarTokens = HaveSimilarVariantTokens(comp.DistinguishingTokens, reference.DistinguishingTokens);

            if (distOverlap || distDice >= 0.50 || similarTokens)
            {
                var reviewScore = (decimal)Math.Round(Math.Min(0.95, baseScore + 0.10), 4);
                var reason = $"Review candidate ({reviewScore:P0}): matching brand, size & format with variant differences '{comp.Title}' vs '{reference.Title}'";
                return new MatchEvaluation(MatchDecision.Review, reviewScore, reason);
            }
        }

        return new MatchEvaluation(MatchDecision.None, 0m, "Insufficient similarity or attribute mismatch");
    }

    private static bool HaveSimilarVariantTokens(HashSet<string> tokens1, HashSet<string> tokens2)
    {
        if (tokens1.Count == 0 || tokens2.Count == 0) return false;
        if (tokens1.Intersect(tokens2).Any()) return true;

        foreach (var t1 in tokens1)
        {
            foreach (var t2 in tokens2)
            {
                if (t1.Length >= 4 && t2.Length >= 4 &&
                    (t1.StartsWith(t2, StringComparison.OrdinalIgnoreCase) || t2.StartsWith(t1, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (TitleSimilarity.NormalizedLevenshtein(t1, t2) >= 0.70)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreTokensSimilar(string t1, string t2)
    {
        if (string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase)) return true;
        if (t1.Length >= 4 && t2.Length >= 4)
        {
            if (t1.StartsWith(t2, StringComparison.OrdinalIgnoreCase) || t2.StartsWith(t1, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return TitleSimilarity.NormalizedLevenshtein(t1, t2) >= 0.75;
    }

    private static bool IsSubstantiveModifier(string token)
    {
        var t = token.ToLowerInvariant();
        return t is "100%" or "100" or "arabica" or "robusta" or "blend" or "premium" or "specialty" or "quality";
    }

    public static HashSet<string> ExtractDistinguishingTokens(
        string title,
        string? brand,
        int? weightGrams,
        int? unitCount,
        int? volumeMl,
        CanonicalCategory category = CanonicalCategory.Other,
        IReadOnlySet<string>? genericTokens = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return [];

        genericTokens ??= DefaultGenericTokens;

        var decoded = TitleNormalizer.Decode(title);
        var asciiDigits = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded);
        var normalizedLetters = ArabicTextNormalizer.NormalizeLetters(asciiDigits);

        // Normalize Latin accented letters (e.g. à -> a)
        var text = AccentsRegex.Replace(normalizedLetters, "a");

        // Strip compatibility clauses
        text = CompatibilityRegex.Replace(text, " ");

        // Strip known brand and its aliases
        if (!string.IsNullOrWhiteSpace(brand))
        {
            text = Regex.Replace(text, $@"\b{Regex.Escape(brand)}\b", " ", RegexOptions.IgnoreCase);
            foreach (var kvp in BrandAliases)
            {
                if (string.Equals(kvp.Value, brand, StringComparison.OrdinalIgnoreCase))
                {
                    text = Regex.Replace(text, $@"\b{Regex.Escape(kvp.Key)}\b", " ", RegexOptions.IgnoreCase);
                }
            }
        }

        // Strip size expressions: weight, volume, unit count
        text = Regex.Replace(text, @"\b\d+(?:\.\d+)?\s*(?:kg|g\b|gm\b|gram[s]?|كجم|كيلو(?:جرام|جم)?|جرام|جم|غرام)\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b\d+(?:\.\d+)?\s*(?:l\b|liter[s]?|ml\b|مل|لتر)\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b\d+\s*(?:capsules?|caps?|pods?|pcs?|pieces?|كبسول(?:ة|ه|ات)?|قطع(?:ة|ه|ا)?)\b", " ", RegexOptions.IgnoreCase);

        // Replace non-alphanumeric with space (retaining % if part of 100%)
        text = NonAlphaNumericRegex.Replace(text, " ");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var w in words)
        {
            var cleanWord = w.Trim().ToLowerInvariant();
            if (cleanWord.Length <= 1 && !char.IsDigit(cleanWord[0]))
                continue;

            // If it equals one of the extracted numbers, skip
            if (int.TryParse(cleanWord, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                if (num == weightGrams || num == unitCount || num == volumeMl ||
                    (weightGrams.HasValue && num == (weightGrams.Value / 1000)) ||
                    (volumeMl.HasValue && num == (volumeMl.Value / 1000)))
                {
                    continue;
                }
            }

            if (genericTokens.Contains(cleanWord))
            {
                // In non-coffee categories (e.g. Syrups, Confectionery), "coffee" is a distinguishing flavor token!
                if (category == CanonicalCategory.Other && cleanWord is "coffee" or "caffe" or "cafe" or "قهوة" or "قهوه" or "بن")
                {
                    result.Add(cleanWord);
                }
                continue;
            }

            result.Add(cleanWord);
        }

        return result;
    }

    public static string? DetectBrand(string? vendor, string title, IReadOnlySet<string>? extraBrands = null)
    {
        // 1. Check title first for known brands (after stripping compatibility clauses)
        var cleanTitle = TitleNormalizer.Decode(title);
        var ascii = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(cleanTitle);
        var norm = ArabicTextNormalizer.NormalizeLetters(ascii);
        var stripped = CompatibilityRegex.Replace(norm, " ");

        // Check aliases first (longest match)
        foreach (var kvp in BrandAliases.OrderByDescending(k => k.Key.Length))
        {
            if (Regex.IsMatch(stripped, $@"\b{Regex.Escape(kvp.Key)}\b", RegexOptions.IgnoreCase))
            {
                return kvp.Value;
            }
        }

        // Check base known brands (longest match)
        foreach (var b in BaseKnownBrands.OrderByDescending(b => b.Length))
        {
            if (Regex.IsMatch(stripped, $@"\b{Regex.Escape(b)}\b", RegexOptions.IgnoreCase))
            {
                return BrandAliases.GetValueOrDefault(b, b);
            }
        }

        // 2. Fallback to vendor (if title didn't contain a known brand)
        if (!string.IsNullOrWhiteSpace(vendor))
        {
            var cleanVendor = vendor.Trim().ToLowerInvariant();
            if (!StoreNames.Contains(cleanVendor))
            {
                if (BrandAliases.TryGetValue(cleanVendor, out var canonicalFromAlias))
                    return canonicalFromAlias;

                foreach (var b in BaseKnownBrands)
                {
                    if (cleanVendor.Contains(b))
                        return BrandAliases.GetValueOrDefault(b, b);
                }

                if (extraBrands != null)
                {
                    foreach (var eb in extraBrands)
                    {
                        if (cleanVendor.Contains(eb))
                            return eb;
                    }
                }
            }
        }

        // 3. Fallback to title with extra dynamic brands
        if (extraBrands != null)
        {
            foreach (var eb in extraBrands.OrderByDescending(b => b.Length))
            {
                if (Regex.IsMatch(stripped, $@"\b{Regex.Escape(eb)}\b", RegexOptions.IgnoreCase))
                {
                    return eb;
                }
            }
        }

        return null;
    }

    private static readonly Regex BeansFormatRegex = new(
        @"\b(?:whole\s*beans?|beans?|حبوب|بن\s*حبوب)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GroundFormatRegex = new(
        @"\b(?:ground|مطحون|مطحونة|مطحونه|filter\s*coffee|filter\s*ground)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? ExtractCoffeeFormat(MatchCandidate candidate)
    {
        // 1. Check GrindSize from Tags (which stores grindSizeName) and ProductType first
        var signals = $"{candidate.Tags} {candidate.ProductType}";
        if (!string.IsNullOrWhiteSpace(signals))
        {
            if (signals.Contains("Whole Coffee Beans", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Whole Beans", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Whole Bean", StringComparison.OrdinalIgnoreCase))
            {
                return "Beans";
            }

            if (signals.Contains("Ground", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Espresso", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Turkish", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Moka Pot", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("Filter", StringComparison.OrdinalIgnoreCase) ||
                signals.Contains("French Press", StringComparison.OrdinalIgnoreCase))
            {
                return "Ground";
            }
        }

        // 2. Check title keywords
        return ExtractCoffeeFormat(candidate.Title);
    }

    public static string? ExtractCoffeeFormat(string title)
    {
        var hasGround = GroundFormatRegex.IsMatch(title);
        var hasBeans = BeansFormatRegex.IsMatch(title);

        if (hasGround && !hasBeans) return "Ground";
        if (hasBeans && !hasGround) return "Beans";
        return null;
    }

    public static HashSet<string> ExtractSignificantNumbers(string title, int? weightGrams, int? volumeMl, int? unitCount)
    {
        var decoded = TitleNormalizer.Decode(title);
        var ascii = ArabicTextNormalizer.ConvertArabicIndicDigitsToAscii(decoded);
        var matches = Regex.Matches(ascii, @"\b\d+\b");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            var val = m.Value;
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                // Ignore leading zeroes or negligible
                if (num == 0) continue;

                // Reconcile weight numbers: 1000g vs 1kg, 2000g vs 2kg, etc.
                if (weightGrams.HasValue && (num == weightGrams.Value || (num == (weightGrams.Value / 1000) && weightGrams.Value % 1000 == 0)))
                    continue;

                // Reconcile volume numbers: 1000ml vs 1L, etc.
                if (volumeMl.HasValue && (num == volumeMl.Value || (num == (volumeMl.Value / 1000) && volumeMl.Value % 1000 == 0)))
                    continue;

                // Reconcile unit count
                if (unitCount.HasValue && num == unitCount.Value)
                    continue;

                set.Add(val);
            }
        }

        return set;
    }

    private static HashSet<string> LoadGenericTokens(IConfiguration? configuration)
    {
        try
        {
            var customPath = configuration?["Matching:GenericTokensPath"];
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(customPath)) candidates.Add(customPath);
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "Matching", "generic_tokens.json"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "generic_tokens.json"));
            candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "LazyBean.Application", "Matching", "generic_tokens.json")));

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("genericTokens", out var arr))
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in arr.EnumerateArray())
                        {
                            var s = elem.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                set.Add(s.Trim().ToLowerInvariant());
                        }
                        if (set.Count > 0) return set;
                    }
                }
            }
        }
        catch
        {
        }

        return new HashSet<string>(DefaultGenericTokens, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> DefaultGenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "coffee", "caffe", "caffè", "cafe", "espresso", "capsules", "capsule", "pods", "pod",
        "beans", "bean", "whole", "ground", "powder",
        "tea", "herbal", "infusion", "compatible", "compatibility",
        "pack", "packs", "packet", "packets", "box", "boxes", "bag", "bags", "tin", "tins",
        "can", "cans", "bottle", "bottles", "jar", "jars", "piece", "pieces", "pcs", "caps",
        "cup", "cups", "mug", "mugs", "original", "vertuo", "vertuoline", "dolce", "gusto",
        "dolcegusto", "nespresso", "caffitaly", "filter", "drip", "machine", "machines",
        "maker", "makers", "system", "by", "for", "with", "in", "of", "and", "the", "a", "an",
        "de", "la", "del", "della", "di", "le", "el", "los", "las", "e", "ed",
        "at", "to", "from", "gram", "grams", "kg", "g", "gm", "ml", "l", "liter", "liters",
        "قهوة", "قهوه", "بن", "اسبريسو", "كبسولات", "كبسولة", "كبسوله", "حبوب", "حب",
        "مطحون", "مطحونة", "مطحونه", "شاي", "شاى", "اعشاب", "أعشاب",
        "متوافق", "متوافقة", "متوافقه", "مع", "علبة", "علبه",
        "باكيت", "كيس", "أكياس", "اكياس", "برطمان", "زجاجة", "زجاجه", "حبات", "حبة", "حبه",
        "قطع", "قطعة", "قطعه", "كوب", "أكواب", "اكواب", "اوريجينال", "فيرتو", "دولتشي", "جوستو",
        "نسبريسو", "كافيتالي", "ماكينة", "ماكينه", "الة", "آلة", "صانعة", "نظام", "من", "في",
        "فى", "على", "و", "جرام", "جم", "غرام", "كجم", "كيلو", "مل", "لتر"
    };

    public sealed class MatchCandidate
    {
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public Guid CompetitorId { get; set; }
        public string CompetitorKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string NormalizedTitle { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? ProductType { get; set; }
        public string? Tags { get; set; }
        public string? Roast { get; set; }
        public string? Vendor { get; set; }
        public CanonicalCategory CanonicalCategory { get; set; }
        public int? WeightGrams { get; set; }
        public int? UnitCount { get; set; }
        public int? VolumeMl { get; set; }
        public decimal Price { get; set; }
        public string CanonicalUrl { get; set; } = string.Empty;
        public HashSet<string> Tokens { get; set; } = [];
        public string? Brand { get; set; }
        public string? CapsuleSystem { get; set; }
        public string BeverageType { get; set; } = "Coffee";
        public HashSet<string> DistinguishingTokens { get; set; } = [];
    }
}
