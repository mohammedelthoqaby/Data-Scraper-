using LazyBean.Infrastructure.External;
using LazyBean.Infrastructure.External.Google;
using LazyBean.Infrastructure.External.Http;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Infrastructure.External.Woo;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collector.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Interfaces;
using LazyBean.Infrastructure.Ads;
using LazyBean.Infrastructure.Caching;
using LazyBean.Infrastructure.Collector;
using LazyBean.Infrastructure.Collection.Adapters;
using LazyBean.Infrastructure.Collection.Evidence;
using LazyBean.Infrastructure.Collection.Orchestration;
using LazyBean.Infrastructure.Collection.Scheduling;
using LazyBean.Infrastructure.HealthChecks;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Ai.RateLimiting;
using LazyBean.Infrastructure.Alerts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;

namespace LazyBean.Infrastructure;

public static class DependencyInjection
{
    // The server's infrastructure: the database-backed services, and the external collectors (HTTP sources and adapters).
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services.AddPersistence(configuration).AddExternalCollectors();

    // Everything that needs the database, and the server-only services (AI, alerts, scheduling, the Graph and Google syncs).
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
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

        // Data Protection keys live on disk (App_Data/keys by default, outside wwwroot) so encrypted
        // provider keys still decrypt after an IIS recycle or a redeploy. The path is set in Program.cs.
        var keyDirectory = configuration["DataProtection:KeyDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "keys");
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("LazyBean");

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICollectorDeviceAuthenticator, CollectorDeviceAuthenticator>();
        services.AddScoped<ICollectorJobService, CollectorJobService>();
        services.AddScoped<IAdsIngestionService, AdsIngestionService>();
        services.AddSingleton(configuration.GetSection("Ads").Get<LazyBean.Application.Ads.Common.AdsOptions>()
            ?? new LazyBean.Application.Ads.Common.AdsOptions());
        services.AddHostedService<LazyBean.Infrastructure.Ads.AdsBackfillService>();
        services.AddScoped<LazyBean.Application.Common.Interfaces.IAdThumbnailService, LazyBean.Infrastructure.Ads.AdThumbnailService>();
        services.AddHostedService<LazyBean.Infrastructure.Ads.AdThumbnailRetentionService>();
        services.AddSingleton<ICollectorServerFallback, CollectorServerFallback>();
        services.AddHostedService<CollectorMaintenanceService>();

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

        // The server keeps its evidence in the gzip store; the desktop app's no-op store is only the fallback (AddExternalCollectors).
        services.AddSingleton<IEvidenceStore, GzipEvidenceStore>();

        services.AddScoped<ICompetitorProductIngestionService, CompetitorProductIngestionService>();

        services.AddScoped<ICollectionRunExecutor, CollectionRunExecutor>();
        services.AddHttpClient<IMetaAdLibraryClient, MetaAdLibraryClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IGoogleAdsSyncService, GoogleAdsSyncService>();

        // AI: Groq (primary) and Gemini (fallback), routed by AiClient behind IAiClient.
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddSingleton<AiRateGate>();
        services.AddHttpClient<GroqClient>();
        services.AddHttpClient<GeminiClient>();
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<GroqClient>());
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<GeminiClient>());
        services.AddScoped<IAiKeyProtector, AiKeyProtector>();
        services.AddScoped<IAiClient, AiClient>();
        services.AddScoped<IAiSettingsStore, AiSettingsStore>();
        services.AddScoped<LazyBean.Application.Common.Interfaces.ICreativeImageReader, LazyBean.Infrastructure.Ai.GeminiCreativeImageReader>();
        services.AddScoped<LazyBean.Application.Ads.Common.CreativeVisionService>();
        services.AddScoped<IAiTriageService, AiReviewTriageService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddSingleton<ICollectionProgressTracker, CollectionProgressTracker>();
        services.AddSingleton<IDiagnosticsErrorBuffer, LazyBean.Infrastructure.Diagnostics.DiagnosticsErrorBuffer>();
        services.AddSingleton<IRunQueue, ChannelRunQueue>();
        services.AddSingleton<IRunCancellationTokenRegistry, RunCancellationTokenRegistry>();
        services.AddHostedService<CollectionRunBackgroundService>();
        services.AddHostedService<SelfKeepAliveHostedService>();
        services.AddSingleton<LazyBean.Application.Matching.IMatchingQueue, LazyBean.Infrastructure.Matching.ChannelMatchingQueue>();
        services.AddHostedService<LazyBean.Infrastructure.Matching.MatchingBackgroundService>();

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
