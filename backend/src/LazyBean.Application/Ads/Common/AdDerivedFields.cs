using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Ads.Common;

// The fields an ad gets from its own text and landing link: the advertised price, the bundle quantity, the product it
// is linked to, how the link was made, why it is not linked, and (Google) the format. Ingest and the backfill both use
// these rules. Bump CurrentVersion when the rules change, so the backfill recomputes existing ads.
public static class AdDerivedFields
{
    public const int CurrentVersion = 4;

    public static void Apply(Ad ad, IReadOnlyList<LinkableProduct> catalog)
    {
        // A Google ad's title is the advertiser's name, not the copy of the ad, so it never describes a product.
        var (title, body) = ad.Platform == AdPlatform.Google
            ? ((string?)null, (string?)null)
            : (ad.Title, ad.Body);

        var offer = AdPriceParser.ParseOffer(title, body);
        var link = AdProductLinker.Link(new AdLinkInput(ad.LinkUrl, title, body), catalog);

        var productId = link.ProductId;
        var method = link.Method;
        var hasAiName = !string.IsNullOrWhiteSpace(ad.AiReadName);
        if (productId is null && hasAiName)
        {
            // The AI-read name is linked only as strictly as a title: a full product name, and only one product.
            var aiLink = AdProductLinker.Link(new AdLinkInput(null, ad.AiReadName, null), catalog);
            if (aiLink.ProductId is { } aiProductId)
            {
                productId = aiProductId;
                method = "ai-read";
            }
        }

        ad.AdvertisedPrice = offer?.Price;
        ad.AdvertisedQuantity = offer?.Quantity;
        ad.ProductId = productId;
        ad.LinkMethod = method;
        ad.LinkReason = productId is null ? AdLinkReasons.Classify(ad.LinkUrl, title, body, hasAiName) : null;

        if (ad.Platform == AdPlatform.Google)
        {
            ad.CreativeFormat = GoogleCreativeFormat.Resolve(ad.CreativeFormat, ad.PreviewUrl);
        }

        ad.LinkVersion = CurrentVersion;
    }
}
