namespace LazyBean.Application.Collector.Exceptions;

// A failure from a source (Meta, Google or a store) that the collection use cases classify. The adapters translate their own
// exceptions into these and keep the message, so the classification is the same whichever adapter raised it.
public class CollectorSourceException(string message, Exception? inner = null) : Exception(message, inner);

// The source rate-limited the requests.
public sealed class CollectorRateLimitException(string message) : CollectorSourceException(message);

// The source asked for a login or a verification.
public sealed class CollectorAuthenticationException(string message) : CollectorSourceException(message);

// The session the requests used has expired.
public sealed class CollectorSessionExpiredException(string message) : CollectorSourceException(message);

// The request failed on the way: a network error, a timeout, or a refusal from the host (for example an image host).
public sealed class CollectorTransportException(string message, Exception? inner = null) : CollectorSourceException(message, inner);
