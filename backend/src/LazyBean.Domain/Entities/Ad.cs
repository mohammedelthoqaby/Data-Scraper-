using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Ad : BaseEntity, IAggregateRoot
{
    public Guid CompetitorId { get; set; }
    public Guid IdentityId { get; set; }
    public Guid? CampaignId { get; set; }
    public string ExternalAdId { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public string? SnapshotUrl { get; set; }
    public DateTimeOffset? DeliveryStartDate { get; set; }
    public DateTimeOffset? DeliveryStopDate { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public AdStatus Status { get; set; } = AdStatus.Active;
    public string CreativeHash { get; set; } = string.Empty;
    public string? RawDataJson { get; set; }
    public int MissingCount { get; set; } = 0;
    public Guid? ProductId { get; set; }
    // Where the creative is shown (Google Ads Transparency preview image or HTML preview) and its format.
    public string? PreviewUrl { get; set; }
    public string? CreativeFormat { get; set; }
    // The price the ad advertises, and for a bundle the number of units it is for (a single price has 1).
    public decimal? AdvertisedPrice { get; set; }
    public int? AdvertisedQuantity { get; set; }
    // How the product link was made: handle, url, title, or none (not linked).
    public string? LinkMethod { get; set; }
    // Version of the link and price rules that produced the fields above. Rows below the current version are
    // recomputed by the backfill.
    public int LinkVersion { get; set; }

    // The creative's own copy and variations, read from its detail (Google). Null until a detail is read or when the detail
    // does not carry them. DetailFetchedAt is when the detail was read; DetailLastShownAt is the last-shown time it reported.
    public string? Headline { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }
    public DateTimeOffset? DetailLastShownAt { get; set; }
    public string? PreviewVariationsJson { get; set; }

    // Why an ad is not linked to a product (AdLinkReasons), or null when it is linked.
    public string? LinkReason { get; set; }

    // Set by the optional Gemini read of an unlinked image ad (off by default). Marked "AI-read" on the ads page.
    public string? AiReadName { get; set; }
    public decimal? AiReadPrice { get; set; }
    public DateTimeOffset? AiReadAt { get; set; }
}
