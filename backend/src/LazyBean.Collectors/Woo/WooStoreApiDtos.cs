using System.Text.Json.Serialization;

namespace LazyBean.Collectors.Woo;

// Shape confirmed against a genuine capture of
// https://strongcoffee-eg.com/wp-json/wc/store/v1/products (see tests/fixtures/README.md).
public sealed record WooProduct(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("permalink")] string Permalink,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("is_in_stock")] bool IsInStock,
    [property: JsonPropertyName("categories")] List<WooCategory> Categories,
    [property: JsonPropertyName("prices")] WooPrices Prices,
    [property: JsonPropertyName("variations")] List<WooVariationRef> Variations);

public sealed record WooCategory([property: JsonPropertyName("name")] string Name);

// The listing's own variations[] entries carry the attribute name/value (e.g. "color":
// "black") - this is the only place that's populated; the variation detail endpoint
// (WooVariationDetail below) returns an empty attributes array for the same variation.
public sealed record WooVariationRef(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("attributes")] List<WooAttribute> Attributes);

// Minor-unit integer strings (e.g. "275000" at currency_minor_unit=2 -> 2750.00 EGP).
public sealed record WooPrices(
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("regular_price")] string RegularPrice,
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("currency_minor_unit")] int CurrencyMinorUnit);

// A variation is itself fetched as a full product detail: GET /products/{variationId}.
// Its own "attributes" field is empty on the real site - the attribute value comes from
// the listing's WooVariationRef.Attributes instead; "variation" (e.g. "color: black") is
// kept as a fallback title source for stores where the listing doesn't carry it either.
public sealed record WooVariationDetail(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("is_in_stock")] bool IsInStock,
    [property: JsonPropertyName("variation")] string? Variation,
    [property: JsonPropertyName("prices")] WooPrices Prices);

public sealed record WooAttribute([property: JsonPropertyName("value")] string Value);
