using System.Text.Json;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Ads.Common;

// Stores what a creative's detail said, and derives the link again from the copy and the landing link.
public static class GoogleCreativeDetailApplier
{
    public const int MaxHeadlineLength = 500;
    public const int MaxDescriptionLength = 4000;
    public const int MaxUrlLength = 2000;

    // A Google creative needs its detail when none was read, or when the listing has shown it again since that read.
    public static bool NeedsDetail(Ad ad) =>
        ad.Platform == AdPlatform.Google &&
        (ad.DetailFetchedAt is null || ad.DetailLastShownAt is null || ad.LastSeenAt > ad.DetailLastShownAt);

    public static void Apply(Ad ad, GoogleCreativeDetailDto detail, DateTimeOffset now, IReadOnlyList<LinkableProduct> catalog)
    {
        ad.Headline = Clean(detail.Headline, MaxHeadlineLength);
        ad.Description = Clean(detail.Description, MaxDescriptionLength);

        var landing = Clean(detail.LandingUrl, MaxUrlLength);
        if (landing is not null)
        {
            ad.LinkUrl = landing;
        }

        ad.PreviewUrl ??= Clean(detail.PreviewUrl, MaxUrlLength);

        var variations = detail.Variations
            .Select(v => Clean(v, MaxUrlLength))
            .Where(v => v is not null)
            .Select(v => v!)
            .Take(GoogleCreativeDetailParser.MaxVariations)
            .ToList();
        ad.PreviewVariationsJson = JsonSerializer.Serialize(variations);

        ad.DetailFetchedAt = now;
        ad.DetailLastShownAt = detail.LastShownAt ?? ad.LastSeenAt;

        AdDerivedFields.Apply(ad, catalog);
    }

    private static string? Clean(string? value, int maxLength) =>
        GoogleAdNormalizer.Truncate(GoogleAdNormalizer.Clean(value), maxLength);
}
