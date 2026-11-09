using LazyBean.Application.Ads.Dtos;

namespace LazyBean.Application.Collector.Dtos;

// The approved accounts this PC collects: Meta pages and Google advertisers, as the server lists them.
public sealed record CollectorIdentities(
    IReadOnlyList<MetaIdentityDto> Meta,
    IReadOnlyList<GoogleIdentityDto> Google);
