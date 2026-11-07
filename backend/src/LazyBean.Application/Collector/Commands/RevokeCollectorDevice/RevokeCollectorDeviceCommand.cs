using MediatR;

namespace LazyBean.Application.Collector.Commands.RevokeCollectorDevice;

// Revoking stops the device token working immediately and returns any job the device was
// holding to the queue, so another paired app can pick it up.
public record RevokeCollectorDeviceCommand(Guid DeviceId) : IRequest;
