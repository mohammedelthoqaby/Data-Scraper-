using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Domain.Interfaces;

namespace LazyBean.Infrastructure.Collector;

public class CollectorDeviceAuthenticator(IUnitOfWork unitOfWork, TimeProvider clock) : ICollectorDeviceAuthenticator
{
    // Heartbeat writes are throttled: every collector poll would otherwise write to the database.
    private static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(5);

    public async Task<CollectorDeviceIdentity?> AuthenticateAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var hash = CollectorTokens.Hash(rawToken);
        var device = (await unitOfWork.CollectorDevices.FindAsync(d => d.TokenHash == hash, ct)).FirstOrDefault();
        if (device is null || device.IsRevoked)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (device.LastSeenAt is null || now - device.LastSeenAt.Value >= TouchInterval)
        {
            device.Touch(now);
            unitOfWork.CollectorDevices.Update(device);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return new CollectorDeviceIdentity(device.Id, device.Name, device.ScopeList);
    }
}
