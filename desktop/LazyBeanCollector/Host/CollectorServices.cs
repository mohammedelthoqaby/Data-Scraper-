using LazyBean.Infrastructure.External;
using Microsoft.Extensions.DependencyInjection;

namespace LazyBean.Collector.App.Host;

// The composition root for collection. The app calls AddExternalCollectors(), the same HTTP side the server uses (the shared
// HTTP policy, robots.txt, the store, Google and Meta sources), so the app and the server run one set of rules. The app never
// registers the database: it has no connection string and no DbContext.
public static class CollectorServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExternalCollectors();
        return services.BuildServiceProvider();
    }
}
