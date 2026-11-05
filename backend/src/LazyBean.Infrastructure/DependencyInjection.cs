using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Interfaces;
using LazyBean.Infrastructure.Ads;
using LazyBean.Infrastructure.Caching;
using LazyBean.Infrastructure.Collection.Adapters;
using LazyBean.Infrastructure.Collection.Evidence;
using LazyBean.Infrastructure.Collection.Http;
using LazyBean.Infrastructure.Collection.Orchestration;
using LazyBean.Infrastructure.Collection.Scheduling;
using LazyBean.Infrastructure.HealthChecks;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Alerts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace LazyBean.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A single DbContextFactory registration backs both: the scoped LazyBeanDbContext
        // used everywhere else in the app (created fresh per scope from the factory below),
        // and the IDbContextFactory<LazyBeanDbContext> the collection pipeline injects
        // directly so each concurrently-running competitor gets its own DbContext instance
        // (EF Core doesn't allow one DbContext to be used from multiple threads at once).
        // Calling AddDbContext and AddDbContextFactory separately for the same TContext
        // registers DbContextOptions<T> twice with conflicting lifetimes and fails DI
        // validation - this single registration avoids that.
        services.AddDbContextFactory<LazyBeanDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LazyBeanDbContext>>().CreateDbContext());
        services.AddScoped<IReadDbContext>(sp => sp.GetRequiredService<LazyBeanDbContext>());

        services.AddDataProtection()
            .PersistKeysToDbContext<LazyBeanDbContext>()
            .SetApplicationName("LazyBean");

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        var redisConnection = configuration.GetConnectionString("Redis");
        var redisConfigured = !string.IsNullOrWhiteSpace(redisConnection);

        services.AddHybridCache();

        if (redisConfigured)
        {
            // L2 distributed cache shared across instances; HybridCache uses it automatically
            // alongside its built-in in-process L1 cache once IDistributedCache is registered.
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        }

        services.AddScoped<ICacheService, HybridCacheService>();

        var healthChecks = services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres");

        if (redisConfigured)
        {
            healthChecks.AddCheck<RedisHealthCheck>("redis");
        }

        services.AddHttpClient("Collector", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LazyBean-Collector/1.0");
        });

        services.AddSingleton<RobotsTxtCache>();
        services.AddSingleton<IEvidenceStore, GzipEvidenceStore>();
        // Singleton so the per-competitor delay/throttle state (and robots.txt cache) is
        // honored across the whole process, not reset every time a DI scope is created.
        services.AddSingleton<ICollectorHttpClient, CollectorHttpClient>();

        services.AddScoped<ICompetitorAdapter, ShopifyAdapter>();
        services.AddScoped<ICompetitorAdapter, WooStoreApiAdapter>();
        services.AddScoped<ICompetitorAdapter, LazzyBeanAdapter>();
        services.AddScoped<ICompetitorAdapterResolver, CompetitorAdapterResolver>();

        services.AddScoped<ICollectionRunExecutor, CollectionRunExecutor>();
        services.AddHttpClient<IMetaAdLibraryClient, MetaAdLibraryClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // Google AI Studio (Gemini) integration
        services.AddHttpClient("GeminiClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IAiKeyProtector, AiKeyProtector>();
        services.AddScoped<IAiClient, GeminiAiClient>();
        services.AddScoped<IAiTriageService, AiReviewTriageService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddSingleton<IRunQueue, ChannelRunQueue>();
        services.AddSingleton<IRunCancellationTokenRegistry, RunCancellationTokenRegistry>();
        services.AddHostedService<CollectionRunBackgroundService>();
        services.AddHostedService<SelfKeepAliveHostedService>();

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("DailyCollection");
            q.AddJob<DailyCollectionJob>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("DailyCollection-trigger")
                .WithCronSchedule(
                    "0 0 7 * * ?",
                    x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"))));
        });
        services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

        return services;
    }
}
