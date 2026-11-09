using LazyBean.Application.Ads.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace LazyBean.Infrastructure.External.Meta;

// Maps one flattened GraphQL ad object to MetaAdDto. Mirrors the field precedence in the Python
// models.Ad.from_graphql_response + normalize_ad: the first creative is the primary one, and the
// three creative layouts (cards, flat live format, legacy arrays) are tried in that order.
public static class MetaAdParser
{
    private const string AdLibraryIdPrefix = "https://www.facebook.com/ads/library/?id=";

    public static MetaAdDto Parse(JsonObject raw)
    {
        var data = OverlaySnapshot(raw);
        var id = FirstScalar(data, "id", "adArchiveID", "ad_archive_id") ?? string.Empty;
        var creative = BuildPrimaryCreative(data);

        var isActive = ResolveActive(data);
        var snapshotUrl =
            FirstTruthyString(data, "snapshot_url", "snapshotUrl") ??
            FirstTruthyString(data, "ad_snapshot_url", "adSnapshotUrl") ??
            (id.Length > 0 ? AdLibraryIdPrefix + id : null);

        return new MetaAdDto(
            Id: id,
            StartDate: ParseDate(FirstPresent(data, "ad_delivery_start_time", "startDate", "start_date")),
            StopDate: ParseDate(FirstPresent(data, "ad_delivery_stop_time", "endDate", "end_date")),
            Status: isActive ? "Active" : "Inactive",
            Body: creative.Body,
            Title: creative.Title,
            Link: creative.LinkUrl,
            SnapshotUrl: snapshotUrl,
            ImageUrl: creative.ImageUrl ?? creative.OriginalImageUrl ?? creative.ThumbnailUrl,
            PublisherPlatforms: Strings(data, "publisher_platforms"),
            ImageUrls: CardImages(data, creative.ImageUrl ?? creative.OriginalImageUrl ?? creative.ThumbnailUrl));
    }

    private const int MaxCardImages = 5;

    // The primary image first, then each card's image (or a video card's poster), without repeats. Up to five.
    private static IReadOnlyList<string> CardImages(JsonObject data, string? primary)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary))
        {
            images.Add(primary);
        }

        foreach (var card in ObjectArray(data, "cards"))
        {
            var url = FirstTruthyString(card, "resized_image_url", "original_image_url") ?? Scalar(card, "video_preview_image_url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                images.Add(url);
            }
        }

        return images.Distinct(StringComparer.Ordinal).Take(MaxCardImages).ToList();
    }

    private sealed record Creative(
        string? Body,
        string? Title,
        string? LinkUrl,
        string? ImageUrl,
        string? OriginalImageUrl,
        string? ThumbnailUrl);

    private static Creative BuildPrimaryCreative(JsonObject data)
    {
        var cards = ObjectArray(data, "cards");
        if (cards.Count > 0)
        {
            var card = cards[0];
            return new Creative(
                Body: ExtractBody(card["body"]),
                Title: Scalar(card, "title"),
                LinkUrl: Scalar(card, "link_url"),
                ImageUrl: FirstTruthyString(card, "resized_image_url", "original_image_url"),
                OriginalImageUrl: Scalar(card, "original_image_url"),
                ThumbnailUrl: Scalar(card, "video_preview_image_url"));
        }

        var hasFlatFields =
            IsPresent(data, "body") || IsPresent(data, "title") ||
            IsPresent(data, "videos") || IsPresent(data, "images");

        if (hasFlatFields)
        {
            var images = ObjectArray(data, "images");
            var videos = ObjectArray(data, "videos");
            var image = images.Count > 0 ? images[0] : new JsonObject();
            var video = videos.Count > 0 ? videos[0] : new JsonObject();

            return new Creative(
                Body: ExtractBody(data["body"]),
                Title: Scalar(data, "title"),
                LinkUrl: Scalar(data, "link_url"),
                ImageUrl: FirstTruthyString(image, "original_image_url", "resized_image_url"),
                OriginalImageUrl: Scalar(image, "original_image_url"),
                ThumbnailUrl: Scalar(video, "video_preview_image_url"));
        }

        // Legacy layout: parallel arrays of copy, with media and the link on snapshot.cards.
        var bodies = Strings(data, "ad_creative_bodies", "adCreativeBodies");
        var linkTitles = Strings(data, "ad_creative_link_titles", "adCreativeLinkTitles");
        string? imageUrl = null;
        string? originalImageUrl = null;
        string? linkUrl = null;

        if (data["snapshot"] is JsonObject snapshot)
        {
            var snapshotCards = ObjectArray(snapshot, "cards");
            if (snapshotCards.Count > 0)
            {
                var card = snapshotCards[0];
                imageUrl = FirstTruthyString(card, "resized_image_url", "original_image_url");
                originalImageUrl = Scalar(card, "original_image_url");
                linkUrl = Scalar(card, "link_url");
            }
        }

        return new Creative(
            Body: bodies.Count > 0 ? bodies[0] : null,
            Title: linkTitles.Count > 0 ? linkTitles[0] : null,
            LinkUrl: linkUrl,
            ImageUrl: imageUrl,
            OriginalImageUrl: originalImageUrl,
            ThumbnailUrl: null);
    }

    private static bool ResolveActive(JsonObject data)
    {
        var isActive = FirstPresent(data, "is_active", "isActive");
        if (isActive is JsonValue value && value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        var adStatus = FirstTruthyString(data, "ad_status", "adStatus");
        return adStatus is null || adStatus == "ACTIVE";
    }

    private static string? ParseDate(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        try
        {
            if (value.TryGetValue<string>(out var text))
            {
                return DateTimeOffset.TryParse(
                    text.Trim().Replace("Z", "+00:00", StringComparison.Ordinal),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsed)
                    ? Format(parsed)
                    : null;
            }

            if (value.TryGetValue<double>(out var seconds))
            {
                return Format(DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(seconds)));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return null;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string? ExtractBody(JsonNode? body)
    {
        return body switch
        {
            JsonObject obj => Scalar(obj, "text"),
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => null,
        };
    }

    // Snapshot fields fill in top-level keys that are missing or empty, matching from_graphql_response.
    private static JsonObject OverlaySnapshot(JsonObject raw)
    {
        var data = JsonNode.Parse(raw.ToJsonString())!.AsObject();
        if (data["snapshot"] is not JsonObject snapshot)
        {
            return data;
        }

        foreach (var (key, value) in snapshot)
        {
            if (!data.ContainsKey(key) || IsEmpty(data[key]))
            {
                data[key] = JsonNode.Parse(value?.ToJsonString() ?? "null");
            }
        }

        return data;
    }

    private static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonValue value when value.TryGetValue<string>(out var text) => text.Length == 0,
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        _ => false,
    };

    private static bool IsPresent(JsonObject obj, string key) => obj[key] is not null;

    private static JsonNode? FirstPresent(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is { } node)
            {
                return node;
            }
        }

        return null;
    }

    private static string? FirstScalar(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is JsonValue value)
            {
                if (value.TryGetValue<string>(out var text) && text.Length > 0)
                {
                    return text;
                }

                if (value.TryGetValue<long>(out var number) && number != 0)
                {
                    return number.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return null;
    }

    private static string? FirstTruthyString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var text = Scalar(obj, key);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? Scalar(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static List<JsonObject> ObjectArray(JsonObject obj, string key)
    {
        var result = new List<JsonObject>();
        if (obj[key] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject child)
                {
                    result.Add(child);
                }
            }
        }

        return result;
    }

    private static List<string> Strings(JsonObject obj, params string[] keys)
    {
        var result = new List<string>();
        foreach (var key in keys)
        {
            if (obj[key] is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        result.Add(text);
                    }
                }

                return result;
            }
        }

        return result;
    }
}
