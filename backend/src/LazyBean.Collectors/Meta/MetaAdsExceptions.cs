namespace LazyBean.Collectors.Meta;

// Mirrors meta_ads_collector.exceptions. The page collector maps these onto the same
// Blocked / Failed statuses the Python collector reported, so the ingest contract is unchanged.
public class MetaAdsException(string message) : Exception(message);

public sealed class MetaRateLimitException(string message) : MetaAdsException(message);

public sealed class MetaSessionExpiredException(string message) : MetaAdsException(message);

public sealed class MetaAuthenticationException(string message) : MetaAdsException(message);

// A transport failure that survived the session's own retries. It is not a Meta verdict, so the
// page collector retries it at the search level before reporting Failed.
public sealed class MetaTransportException(string message, Exception? inner = null)
    : Exception(message, inner);
