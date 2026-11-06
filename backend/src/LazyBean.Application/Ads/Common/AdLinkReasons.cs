namespace LazyBean.Application.Ads.Common;

// Why an ad is not linked to a product. The code is stored on the ad, and the label is what the ads page shows.
public static class AdLinkReasons
{
    public const string NoProductInfo = "NoProductInfo";
    public const string LinksToHomepage = "LinksToHomepage";
    public const string NoMatch = "NoMatch";

    // Set while the reason has not been worked out yet (an ad stored before the reasons existed).
    public const string Pending = "Pending";

    // The reason an unlinked ad has no product. hasAiName: the optional AI-read named a product, but none in the catalog matches it.
    public static string Classify(string? landingUrl, string? title, string? body, bool hasAiName)
    {
        if (hasAiName)
        {
            return NoMatch;
        }

        var hasText = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body);
        var hasLink = !string.IsNullOrWhiteSpace(landingUrl);
        if (!hasText && !hasLink)
        {
            return NoProductInfo;
        }

        if (hasLink && !AdProductLinker.IsProductPage(landingUrl))
        {
            return LinksToHomepage;
        }

        return NoMatch;
    }

    // The ads page label. A linked ad has no label (null).
    public static string? Label(string? reason, string? competitorName) => reason switch
    {
        null => null,
        NoProductInfo => "No product info in this ad",
        LinksToHomepage => "Links to homepage/collection",
        NoMatch => $"No matching product in {CompetitorName(competitorName)} catalog",
        _ => "Link not checked yet",
    };

    private static string CompetitorName(string? name) => string.IsNullOrWhiteSpace(name) ? "competitor" : name.Trim();
}
