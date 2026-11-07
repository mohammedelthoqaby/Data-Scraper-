namespace LazyBean.Application.Collector.Dtos;

// Returned once, at pairing. The app stores it with DPAPI and never shows it again.
public sealed record PairCollectorDeviceResultDto(
    Guid DeviceId,
    string DeviceName,
    string Token,
    IReadOnlyList<string> Scopes);

public sealed record CollectorDeviceDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Scopes,
    string PairedByUsername,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    bool IsConnected);
