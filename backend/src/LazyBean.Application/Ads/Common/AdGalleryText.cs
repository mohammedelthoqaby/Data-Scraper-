using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Ads.Common;

// The headline and body a card shows for an ad. Google's title is the advertiser's name, so a Google card takes its copy only
// from the creative's detail. The same text decides which ads are variations of one card.
public static class AdGalleryText
{
    public static string? Headline(Ad ad) =>
        ad.Platform == AdPlatform.Google ? ad.Headline : ad.Title ?? ad.Headline;

    public static string? Body(Ad ad) =>
        ad.Platform == AdPlatform.Google ? ad.Description : ad.Body;
}
