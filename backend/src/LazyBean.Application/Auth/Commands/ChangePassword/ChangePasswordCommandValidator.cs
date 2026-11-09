using FluentValidation;

namespace LazyBean.Application.Auth.Commands.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(10).WithMessage("The new password must be at least 10 characters.")
            .MaximumLength(200)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}
