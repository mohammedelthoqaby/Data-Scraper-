using LazyBean.Domain.Entities;

namespace LazyBean.Application.Ads.Common;

// The fields an ad gets from its own text and landing link: the advertised price, the bundle quantity, the product it
// is linked to, and how the link was made. Ingest and the backfill both use these rules. Bump CurrentVersion when the
// rules change, so the backfill recomputes existing ads.
public static class AdDerivedFields
{
    public const int CurrentVersion = 3;

    public static void Apply(Ad ad, IReadOnlyList<LinkableProduct> catalog)
    {
        var offer = AdPriceParser.ParseOffer(ad.Title, ad.Body);
        var link = AdProductLinker.Link(new AdLinkInput(ad.LinkUrl, ad.Title, ad.Body), catalog);

        ad.AdvertisedPrice = offer?.Price;
        ad.AdvertisedQuantity = offer?.Quantity;
        ad.ProductId = link.ProductId;
        ad.LinkMethod = link.Method;
        ad.LinkVersion = CurrentVersion;
    }
}
