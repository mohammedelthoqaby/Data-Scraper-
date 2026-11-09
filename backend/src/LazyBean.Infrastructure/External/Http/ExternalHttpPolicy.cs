namespace LazyBean.Infrastructure.External.Http;

// The one HTTP policy the collection clients share: the identity the app asks the store and page hosts with, and the names of
// the HTTP clients registered for it (AddExternalCollectors). Retries live in HttpRetryHelper and CollectorHttpClient; the
// per-page session of Meta keeps its own cookie jar on purpose.
public static class ExternalHttpPolicy
{
    public const string CollectorClientName = "Collector";

    // The identity the collector uses for store, page and ad requests.
    public const string CollectorUserAgent = "LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)";
}
