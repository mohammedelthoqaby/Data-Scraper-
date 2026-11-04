using FluentValidation;
using MediatR;
using ValidationException = LazyBean.Application.Common.Exceptions.ValidationException;

namespace LazyBean.Application.Common.Behaviors;

// Runs every registered FluentValidation validator for TRequest before the handler executes.
// Any failure short-circuits the pipeline with a ValidationException, which the API's
// exception-handling middleware maps to a 400 response.
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
