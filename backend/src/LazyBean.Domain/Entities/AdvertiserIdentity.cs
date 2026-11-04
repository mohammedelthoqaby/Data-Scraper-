using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class AdvertiserIdentity : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public AdPlatform Platform { get; set; }
    public string ExternalId { get; set; } = string.Empty; // Meta page_id or Google AR id
    public string Name { get; set; } = string.Empty;
    public AdvertiserIdentityStatus Status { get; set; } = AdvertiserIdentityStatus.Pending;
    public string? LibraryUrl { get; set; }
    public string? OwnershipEvidenceUrl { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }

    public void Approve(string? notes = null)
    {
        Status = AdvertiserIdentityStatus.Approved;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
        }
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string? notes = null)
    {
        Status = AdvertiserIdentityStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
        }
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Block(DateTimeOffset until, string? notes = null)
    {
        Status = AdvertiserIdentityStatus.Blocked;
        BlockedUntil = until;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
