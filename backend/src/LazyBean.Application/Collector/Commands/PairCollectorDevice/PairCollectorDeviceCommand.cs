using LazyBean.Application.Collector.Dtos;
using MediatR;

namespace LazyBean.Application.Collector.Commands.PairCollectorDevice;

public record PairCollectorDeviceCommand(string Username, string Password, string DeviceName)
    : IRequest<PairCollectorDeviceResultDto>;
