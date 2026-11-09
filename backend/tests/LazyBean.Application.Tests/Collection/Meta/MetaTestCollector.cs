using LazyBean.Application.Ads.Collection;
using LazyBean.Infrastructure.External.Meta;

namespace LazyBean.Application.Tests.Collection.Meta;

// The page collector built the way the application wires it: the Meta source from the same options, and the waits the
// options carry. Tests use it with MetaAdsOptions.NoWaits and a fake Facebook handler.
internal static class MetaTestCollector
{
    public static MetaPageCollector For(MetaAdsOptions options, HttpMessageHandler? handler) =>
        new(new MetaAdsSource(options, handler), PolicyFrom(options), options.Delay);

    private static MetaCollectionPolicy PolicyFrom(MetaAdsOptions options) => new()
    {
        SearchMaxRetries = options.SearchMaxRetries,
        RateLimitRetryBase = options.RateLimitRetryBase,
        SessionRetryDelay = options.SessionRetryDelay,
        TransportRetryBase = options.TransportRetryBase,
        PageDelay = options.PageDelay,
        PageJitter = options.PageJitter,
    };
}
