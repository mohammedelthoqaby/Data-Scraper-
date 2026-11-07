using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

// A paired LazyBean Collector install. Only the SHA-256 of its device token is stored; the raw
// token is returned once at pairing. Revoking sets RevokedAt, which stops the token working on
// the next request.
public class CollectorDevice : BaseEntity, IAggregateRoot
{
    public const string AdsIngestScope = "ads:ingest";
    public const string StoreIngestScope = "store:ingest";

    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;

    // Space-separated scopes, e.g. "ads:ingest store:ingest".
    public string Scopes { get; set; } = string.Empty;

    public string PairedByUsername { get; set; } = string.Empty;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;

    public IReadOnlyList<string> ScopeList =>
        Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool HasScope(string scope) => ScopeList.Contains(scope, StringComparer.Ordinal);

    public void Touch(DateTimeOffset now)
    {
        LastSeenAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
        UpdatedAt = now;
    }
}
