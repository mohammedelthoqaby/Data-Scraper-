using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace LazyBean.Application.Collector.Queries.GetCollectorDevices;

// A device counts as connected while its app has polled within the heartbeat window.
public class GetCollectorDevicesQueryHandler(IReadDbContext readContext, TimeProvider clock)
    : IRequestHandler<GetCollectorDevicesQuery, IReadOnlyList<CollectorDeviceDto>>
{
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(45);

    public async Task<IReadOnlyList<CollectorDeviceDto>> Handle(GetCollectorDevicesQuery request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var devices = await readContext.Query<CollectorDevice>()
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return devices
            .Select(d => new CollectorDeviceDto(
                d.Id,
                d.Name,
                d.ScopeList,
                d.PairedByUsername,
                d.CreatedAt,
                d.LastSeenAt,
                d.RevokedAt,
                IsConnected: !d.IsRevoked && d.LastSeenAt is { } seen && now - seen <= ConnectedWindow))
            .ToList();
    }
}
