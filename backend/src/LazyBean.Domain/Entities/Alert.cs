using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Alert : BaseEntity, IAggregateRoot
{
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? TitleAr { get; set; }
    public string? ReasonAr { get; set; }
    public string SuggestedAction { get; set; } = string.Empty;
    public string? SuggestedActionAr { get; set; }

    public Guid? ReferenceProductId { get; set; }
    public string? ReferenceProductTitle { get; set; }
    public string? ReferenceProductUrl { get; set; }

    public Guid? CompetitorProductId { get; set; }
    public string? CompetitorProductTitle { get; set; }
    public string? CompetitorProductUrl { get; set; }
    public string? CompetitorName { get; set; }

    public string? AdLibraryUrl { get; set; }
    public bool IsBestseller { get; set; }
    public decimal EgpImpact { get; set; }
    public string MetricValuesJson { get; set; } = "{}";

    public AlertStatus Status { get; set; } = AlertStatus.Active;
    public DateTimeOffset? SnoozedUntil { get; set; }
    public string? DismissNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string IdentityKey { get; set; } = string.Empty;
}
