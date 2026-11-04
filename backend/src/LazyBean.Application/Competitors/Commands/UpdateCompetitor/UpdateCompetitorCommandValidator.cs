using FluentValidation;

namespace LazyBean.Application.Competitors.Commands.UpdateCompetitor;

public class UpdateCompetitorCommandValidator : AbstractValidator<UpdateCompetitorCommand>
{
    public UpdateCompetitorCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500).When(x => x.BaseUrl is not null);
        RuleFor(x => x.MinDelayMs).GreaterThanOrEqualTo(0).When(x => x.MinDelayMs.HasValue);
        RuleFor(x => x.MaxConcurrency).GreaterThan(0).When(x => x.MaxConcurrency.HasValue);
    }
}
