using LazyBean.Application.Collector.Dtos;

namespace LazyBean.Application.Collector.Dtos;

// What a finished job reports back to the server: a one-line summary and, for ads jobs, one row per page.
public sealed record JobOutcome(string Summary, IReadOnlyList<CollectorPageReport>? Pages = null);
