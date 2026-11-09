using LazyBean.Application.Ads.Collection;
using LazyBean.Application.Ads.Ports;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Application.Collector.Ports;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Infrastructure.Collection.Adapters;
using LazyBean.Infrastructure.Collection.Evidence;
using LazyBean.Infrastructure.External.Google;
using LazyBean.Infrastructure.External.Http;
using LazyBean.Infrastructure.External.Images;
using LazyBean.Infrastructure.External.Meta;
using LazyBean.Infrastructure.External.PageResolution;
using LazyBean.Infrastructure.External.Woo;
using LazyBean.Infrastructure.Collection.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LazyBean.Infrastructure.External;

// The HTTP side of collection, used by the server and by the desktop app: the shared HTTP policy (the User-Agent, timeouts,
// robots.txt, the per-competitor delay and the 429/503 retries), the store adapters, the Google and Meta sources, the Facebook
// page lookup, and the image reader. It never touches the database, so the desktop app calls this and nothing else.
public static class ExternalCollectorsServiceCollectionExtensions
{
    public static IServiceCollection AddExternalCollectors(this IServiceCollection services)
    {
        services.AddHttpClient(ExternalHttpPolicy.CollectorClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ExternalHttpPolicy.CollectorUserAgent);
        });
        services.AddHttpClient(FacebookPageIdLookup.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient(AdThumbnailProcessor.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AdThumbnailProcessor.BrowserUserAgent);
        });

        services.AddSingleton<RobotsTxtCache>();
        // Singleton so the per-competitor delay state and the robots.txt cache hold across the whole process.
        services.AddSingleton<ICollectorHttpClient, CollectorHttpClient>();
        services.TryAddSingleton<IEvidenceStore, NoEvidenceStore>();

        services.AddScoped<ICompetitorAdapter, ShopifyAdapter>();
        services.AddScoped<ICompetitorAdapter, WooStoreApiAdapter>();
        services.AddScoped<ICompetitorAdapter, LazzyBeanAdapter>();
        services.AddScoped<ICompetitorAdapterResolver, CompetitorAdapterResolver>();

        services.AddHttpClient<IGoogleAdsTransparencyClient, GoogleAdsTransparencyClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton(MetaAdsOptions.Default);
        services.AddSingleton(MetaCollectionPolicy.Default);
        services.AddSingleton<IMetaAdsSource, MetaAdsSource>();
        services.AddSingleton<IMetaPageCollector, MetaPageCollector>();

        services.AddSingleton<IFacebookPageIdLookup, FacebookPageIdLookup>();
        services.AddSingleton<IAdThumbnailProcessor, AdThumbnailProcessor>();

        return services;
    }
}
