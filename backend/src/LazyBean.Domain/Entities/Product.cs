using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Product : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public int? WeightGrams { get; set; }
    public int? UnitCount { get; set; }
    public string? Category { get; set; }
    public string? ProductType { get; set; }
    public string? VendorId { get; set; }
    public string? Vendor { get; set; }
    public string? Brand { get; set; }
    public string? Tags { get; set; }
    public CanonicalCategory CanonicalCategory { get; set; } = CanonicalCategory.Other;
    public decimal? ClassificationConfidence { get; set; }
    public string? MatchedRule { get; set; }
    public string? Roast { get; set; }
    public string? Origin { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public string? Handle { get; set; }
}
