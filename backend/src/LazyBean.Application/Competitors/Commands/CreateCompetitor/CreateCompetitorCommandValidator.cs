using FluentValidation;
using LazyBean.Domain.Interfaces;

namespace LazyBean.Application.Competitors.Commands.CreateCompetitor;

public class CreateCompetitorCommandValidator : AbstractValidator<CreateCompetitorCommand>
{
    public CreateCompetitorCommandValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9-]+$").WithMessage("Key must be lowercase kebab-case, e.g. 'coffee-zone-misr'.")
            .MustAsync(async (key, ct) => !await unitOfWork.Competitors.KeyExistsAsync(key, ct))
            .WithMessage("A competitor with this key already exists.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        RuleFor(x => x.BaseUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("BaseUrl must be a valid absolute URL.");

        RuleFor(x => x.Platform).IsInEnum();
    }
}
