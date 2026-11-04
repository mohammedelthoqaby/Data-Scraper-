namespace LazyBean.Application.Collection.Normalization;

public static class PriceLogic
{
    // A compare-at price only counts as a discount if it's strictly greater than the
    // selling price - equal or lower compare-at values are data noise, not an offer.
    public static bool IsDiscounted(decimal price, decimal? compareAtPrice) =>
        compareAtPrice is { } value && value > price;
}
