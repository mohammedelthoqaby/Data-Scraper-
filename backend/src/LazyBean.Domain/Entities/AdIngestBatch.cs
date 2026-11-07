using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

// One ingest from the collector for one advertiser account. The counts say what the app sent and what the server
// did with it, so the Ads summary can show ingested, stored and displayed side by side.
public class AdIngestBatch : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public Guid IdentityId { get; set; }
    public AdPlatform Platform { get; set; }
    public int Received { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Duplicate { get; set; }
    public int Skipped { get; set; }
    public string Status { get; set; } = "Success";
    public string? Error { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
