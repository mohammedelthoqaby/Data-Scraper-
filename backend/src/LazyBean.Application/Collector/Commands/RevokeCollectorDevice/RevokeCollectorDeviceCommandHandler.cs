using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Collector.Commands.RevokeCollectorDevice;

public class RevokeCollectorDeviceCommandHandler(IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<RevokeCollectorDeviceCommand>
{
    public async Task Handle(RevokeCollectorDeviceCommand request, CancellationToken ct)
    {
        var device = await unitOfWork.CollectorDevices.GetByIdAsync(request.DeviceId, ct)
            ?? throw new NotFoundException(nameof(CollectorDevice), request.DeviceId);

        var now = clock.GetUtcNow();
        device.Revoke(now);
        unitOfWork.CollectorDevices.Update(device);

        var heldJobs = await unitOfWork.CollectorJobs.FindAsync(
            j => j.ClaimedByDeviceId == request.DeviceId && j.Status == CollectorJobStatus.Claimed, ct);
        foreach (var job in heldJobs)
        {
            job.ReleaseExpiredClaim(now);
            unitOfWork.CollectorJobs.Update(job);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
