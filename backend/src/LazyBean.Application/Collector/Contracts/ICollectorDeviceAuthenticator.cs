namespace LazyBean.Application.Collector.Contracts;

// Resolves a raw device token presented as a bearer credential. Returns null for unknown or
// revoked tokens, which the API turns into 401 so the app asks the user to pair again.
public interface ICollectorDeviceAuthenticator
{
    Task<CollectorDeviceIdentity?> AuthenticateAsync(string rawToken, CancellationToken ct);
}

public sealed record CollectorDeviceIdentity(Guid DeviceId, string Name, IReadOnlyList<string> Scopes);
