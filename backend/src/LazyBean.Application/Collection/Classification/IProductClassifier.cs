using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Classification;

public sealed record ClassificationResult(
    CanonicalCategory Category,
    decimal Confidence,
    string MatchedRule,
    bool RequiresReview = false,
    string? ReviewReason = null);

public interface IProductClassifier
{
    ClassificationResult Classify(
        string title,
        string? productType = null,
        string? vendor = null,
        string? tags = null,
        string? category = null);
}
