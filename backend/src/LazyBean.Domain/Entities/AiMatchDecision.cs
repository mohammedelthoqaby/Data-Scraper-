using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

/// <summary>
/// Cached AI (or guardrail) decision for one competitor/reference pair. The hash covers the key
/// fields of both products (name, brand, size, form, capsule system, roast), so a pair is only
/// asked again when one of those changes. Prices and URLs are not part of the hash.
/// </summary>
public class AiMatchDecision : BaseEntity, IAggregateRoot
{
    public string PairHash { get; set; } = string.Empty;
    public Guid CompetitorVariantId { get; set; }
    public Guid ReferenceVariantId { get; set; }

    /// <summary>"same" | "different" | "unsure".</summary>
    public string Verdict { get; set; } = "unsure";

    public decimal Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>Set when a hard guardrail decided the verdict without calling AI.</summary>
    public string? GuardrailReason { get; set; }

    /// <summary>"groq" | "gemini" | "guardrail".</summary>
    public string Provider { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
