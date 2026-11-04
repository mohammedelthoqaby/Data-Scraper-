using System.Text.Json;
using System.Text.RegularExpressions;
using LazyBean.Application.Collection.Normalization;
using LazyBean.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace LazyBean.Application.Collection.Classification;

public class ProductClassifier : IProductClassifier
{
    private readonly List<CompiledClassificationRule> _rules;

    private static readonly Regex CoffeeLikeRegex = new(
        @"(?:coffee|espresso|cappuccino|latte|arabica|robusta|roast|beans?|قهو[ةه]|بن\b|اسبريسو|كابتشينو|لاتيه|ارابيكا|روبوستا|تحميص|محوج)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ProductClassifier(IConfiguration? configuration = null)
    {
        var rulesPath = configuration?["Collection:ClassificationRulesPath"];
        _rules = LoadRules(rulesPath);
    }

    public ProductClassifier(string jsonContent)
    {
        _rules = ParseRules(jsonContent);
    }

    public ClassificationResult Classify(
        string title,
        string? productType = null,
        string? vendor = null,
        string? tags = null,
        string? category = null)
    {
        var decodedTitle = TitleNormalizer.Decode(title);
        var normalizedTitle = TitleNormalizer.NormalizeForMatching(title);

        var normalizedProductType = string.IsNullOrWhiteSpace(productType) ? null : TitleNormalizer.NormalizeForMatching(productType);
        var normalizedTags = string.IsNullOrWhiteSpace(tags) ? null : TitleNormalizer.NormalizeForMatching(tags);
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : TitleNormalizer.NormalizeForMatching(category);

        foreach (var rule in _rules)
        {
            // If negative patterns match the title or product type, this rule is skipped
            if (rule.NegativeTitleRegexes.Any(r => r.IsMatch(decodedTitle) || r.IsMatch(normalizedTitle) || (normalizedProductType != null && r.IsMatch(normalizedProductType))))
            {
                continue;
            }

            var matched = false;

            // 1. Check title patterns
            if (rule.TitleRegexes.Any(r => r.IsMatch(decodedTitle) || r.IsMatch(normalizedTitle)))
            {
                matched = true;
            }

            // 2. Check productType patterns
            if (!matched && normalizedProductType is not null && rule.ProductTypeRegexes.Any(r => r.IsMatch(productType!) || r.IsMatch(normalizedProductType)))
            {
                matched = true;
            }

            // 3. Check tag / category patterns
            if (!matched && normalizedTags is not null && rule.TagRegexes.Any(r => r.IsMatch(tags!) || r.IsMatch(normalizedTags)))
            {
                matched = true;
            }

            if (!matched && normalizedCategory is not null && rule.TagRegexes.Any(r => r.IsMatch(category!) || r.IsMatch(normalizedCategory)))
            {
                matched = true;
            }

            // 4. Check vendor patterns
            if (!matched && !string.IsNullOrWhiteSpace(vendor) && rule.VendorRegexes.Any(r => r.IsMatch(vendor)))
            {
                matched = true;
            }

            if (matched)
            {
                var isCoffee = CoffeeLikeRegex.IsMatch(decodedTitle) || CoffeeLikeRegex.IsMatch(normalizedTitle);
                var requiresReview = isCoffee && (rule.Category == CanonicalCategory.Other || rule.Confidence < 0.60m);
                var reviewReason = requiresReview
                    ? $"Coffee keyword detected in '{decodedTitle}' but classified as '{rule.Category}' with confidence {rule.Confidence:0.00}."
                    : null;

                return new ClassificationResult(rule.Category, rule.Confidence, rule.Id, requiresReview, reviewReason);
            }
        }

        // Unmatched fallback
        var isCoffeeLike = CoffeeLikeRegex.IsMatch(decodedTitle) || CoffeeLikeRegex.IsMatch(normalizedTitle);
        if (isCoffeeLike)
        {
            return new ClassificationResult(
                CanonicalCategory.Other,
                0.40m,
                "default:unmatched",
                RequiresReview: true,
                ReviewReason: $"Title '{decodedTitle}' contains coffee-like keywords but did not match any classification rule with high confidence.");
        }

        return new ClassificationResult(CanonicalCategory.Other, 0.70m, "default:unmatched");
    }

    private static List<CompiledClassificationRule> LoadRules(string? customPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            candidates.Add(customPath);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Collection", "Classification", "classification_rules.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "classification_rules.json"));

        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "LazyBean.Application", "Collection", "Classification", "classification_rules.json"));
        candidates.Add(projectDir);

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return ParseRules(json);
            }
        }

        return [];
    }

    private static List<CompiledClassificationRule> ParseRules(string json)
    {
        var parsed = JsonSerializer.Deserialize<ClassificationRulesConfigFile>(json);
        if (parsed?.Rules is null)
        {
            return [];
        }

        return parsed.Rules
            .OrderByDescending(r => r.Priority)
            .Select(r => new CompiledClassificationRule(
                r.Id,
                r.Category,
                r.Priority,
                r.Confidence,
                r.TitlePatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToList(),
                r.NegativeTitlePatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToList(),
                r.ProductTypePatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToList(),
                r.TagPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToList(),
                r.VendorPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToList()))
            .ToList();
    }

    private sealed record CompiledClassificationRule(
        string Id,
        CanonicalCategory Category,
        int Priority,
        decimal Confidence,
        List<Regex> TitleRegexes,
        List<Regex> NegativeTitleRegexes,
        List<Regex> ProductTypeRegexes,
        List<Regex> TagRegexes,
        List<Regex> VendorRegexes);
}
