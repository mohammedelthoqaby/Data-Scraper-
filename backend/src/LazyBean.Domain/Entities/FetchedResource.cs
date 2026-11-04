using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

// One row per HTTP request the collector made - evidence trail, not just a log line.
// The raw response body is gzip'd to disk/volume at StoragePath; ContentHash lets us
// detect "nothing changed since last run" without re-downloading.
public class FetchedResource : BaseEntity, IAggregateRoot
{
    public Guid RunId { get; set; }
    public Guid CompetitorId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int HttpStatus { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ContentHash { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}
