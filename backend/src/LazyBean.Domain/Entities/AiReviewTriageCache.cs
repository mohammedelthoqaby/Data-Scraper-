using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class AiReviewTriageCache : BaseEntity, IAggregateRoot
{
    public string PairHash { get; set; } = string.Empty;
    public string Verdict { get; set; } = "unsure"; // "same" | "different" | "unsure"
    public decimal Confidence { get; set; } = 0.5m;
    public string Reason { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public DateTimeOffset AnalyzedAt { get; set; } = DateTimeOffset.UtcNow;
}
