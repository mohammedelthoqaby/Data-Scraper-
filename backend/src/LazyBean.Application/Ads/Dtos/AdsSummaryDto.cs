namespace LazyBean.Application.Ads.Dtos;

// Per competitor and platform: what the app sent (Ingested, the latest batch of each approved account), what is stored,
// what the Ads page shows under its default filter (Displayed), and the coverage line: when it last synced, or the error.
public record AdsSummaryRowDto(
    Guid CompetitorId,
    string CompetitorName,
    bool IsReference,
    string Platform,
    int Identities,
    int Ingested,
    int Stored,
    int Displayed,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

public record AdsSummaryDto(
    IReadOnlyList<AdsSummaryRowDto> Rows,
    DateTimeOffset GeneratedAt);
