using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Contracts;

// One adapter per Platform, not per site - Shopify/WooCommerce stores all speak the same
// JSON API shape, so site-specific classes only appear if a site genuinely needs overrides.
public interface ICompetitorAdapter
{
    CompetitorPlatform Platform { get; }

    IAsyncEnumerable<RawProduct> FetchAsync(Competitor competitor, CancellationToken ct);
}
