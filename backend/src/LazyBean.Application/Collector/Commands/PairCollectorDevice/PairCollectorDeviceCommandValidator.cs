using FluentValidation;

namespace LazyBean.Application.Collector.Commands.PairCollectorDevice;

public class PairCollectorDeviceCommandValidator : AbstractValidator<PairCollectorDeviceCommand>
{
    public PairCollectorDeviceCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(80);
    }
}
