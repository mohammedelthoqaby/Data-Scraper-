using FluentValidation;
using LazyBean.Application.Collection.Classification;
using LazyBean.Application.Common.Behaviors;
using LazyBean.Application.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace LazyBean.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddSingleton<IProductClassifier, ProductClassifier>();
        services.AddScoped<ProductMatcher>();
        services.AddScoped<IProductMatcher>(sp => sp.GetRequiredService<ProductMatcher>());
        services.AddScoped<LazyBean.Application.Matching.AiMatching.IAiMatchingService, LazyBean.Application.Matching.AiMatching.AiMatchingService>();

        return services;
    }
}
