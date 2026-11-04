namespace LazyBean.Application.Collection.Contracts;

// What an adapter hands back for one catalog item, before normalization/validation.
// Deliberately flat and platform-agnostic - Shopify and WooCommerce both map onto this.
public sealed record RawProduct(
    string ExternalId,
    string Title,
    string CanonicalUrl,
    string? Category,
    IReadOnlyList<RawVariant> Variants,
    string? ProductType = null,
    string? Vendor = null,
    string? Tags = null,
    string? VendorId = null,
    int? WeightGrams = null,
    string? Roast = null,
    string? Brand = null);

public sealed record RawVariant(
    string ExternalId,
    string? Title,
    decimal Price,
    decimal? CompareAtPrice,
    string Currency,
    bool? InStock,
    string? Sku);
