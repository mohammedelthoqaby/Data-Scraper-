using LazyBean.Application.Collector.Dtos;
using MediatR;

namespace LazyBean.Application.Collector.Queries.GetCollectorDevices;

public record GetCollectorDevicesQuery : IRequest<IReadOnlyList<CollectorDeviceDto>>;
