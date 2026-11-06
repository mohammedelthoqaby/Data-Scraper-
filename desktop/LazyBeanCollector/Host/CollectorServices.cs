using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collectors.Google;
using LazyBean.Collectors.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LazyBean.Collector.App.Host;

// The composition root for collection. Uses the same HTTP policy the server uses (per-competitor delay,
// robots.txt, 429/503 retry) with the app's own IP address.
public static class CollectorServices
{
    public const string UserAgent = "LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)";

    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHttpClient("Collector", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });

        services.AddSingleton<RobotsTxtCache>();
        services.AddSingleton<IEvidenceStore, NoEvidenceStore>();
        services.AddSingleton<ICollectorHttpClient, CollectorHttpClient>();

        services.AddHttpClient<IGoogleAdsTransparencyClient, GoogleAdsTransparencyClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services.BuildServiceProvider();
    }
}
