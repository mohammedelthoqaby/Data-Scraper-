using System.Text.Json.Serialization;

namespace LazyBean.Infrastructure.Collection.Adapters;

// Matches a genuine capture of https://coffeezonemisr.com/products.json (see
// tests/fixtures/shopify_coffeezonemisr_page1.json) - price/compare_at_price are decimal
// strings, and "grams" is unreliably 0 on these stores, so weight comes from the title instead.
public sealed record ShopifyProductsResponse([property: JsonPropertyName("products")] List<ShopifyProduct> Products);

public sealed record ShopifyProduct(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("handle")] string Handle,
    [property: JsonPropertyName("product_type")] string? ProductType,
    [property: JsonPropertyName("vendor")] string? Vendor,
    [property: JsonPropertyName("tags")] System.Text.Json.JsonElement TagsRaw,
    [property: JsonPropertyName("variants")] List<ShopifyVariant> Variants)
{
    public string? FormattedTags =>
        TagsRaw.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Array => string.Join(", ", TagsRaw.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s))),
            System.Text.Json.JsonValueKind.String => TagsRaw.GetString(),
            _ => null
        };
}

public sealed record ShopifyVariant(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("compare_at_price")] string? CompareAtPrice,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("available")] bool? Available);
