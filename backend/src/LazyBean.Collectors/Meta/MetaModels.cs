using System.Text.Json.Serialization;

namespace LazyBean.Collectors.Meta;

// One ad in the shape the LazyBean ads ingest endpoint expects (same keys as the Python normalize_ad).
public sealed record MetaAdDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("startDate")] string? StartDate,
    [property: JsonPropertyName("stopDate")] string? StopDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("snapshotUrl")] string? SnapshotUrl,
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonPropertyName("publisherPlatforms")] IReadOnlyList<string> PublisherPlatforms,
    // The card images of the ad, primary first (a carousel has several; a video card contributes its poster). Up to five.
    [property: JsonPropertyName("imageUrls")] IReadOnlyList<string>? ImageUrls = null);

// Outcome of collecting one Meta page ID. Status is "Success", "Blocked" (rate limit, session or
// verification challenge) or "Failed", exactly as the Python collector reported it.
public sealed record MetaPageResult(
    string PageId,
    string Country,
    string Status,
    IReadOnlyList<MetaAdDto> Ads,
    string? Error,
    string? ErrorType)
{
    public int TotalCollected => Ads.Count;
}
