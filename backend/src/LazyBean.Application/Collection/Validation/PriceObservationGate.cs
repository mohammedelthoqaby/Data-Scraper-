namespace LazyBean.Application.Collection.Validation;

public enum PriceObservationDecision
{
    Accept,
    SendToReview
}

public sealed record GateResult(PriceObservationDecision Decision, string? Reason = null);

// Pure gate: decides whether an observed price is trustworthy enough to become a
// PriceObservation, or suspicious enough to go to the ReviewItem queue instead.
// No I/O - the caller (orchestrator) supplies the last known price for this variant.
public static class PriceObservationGate
{
    private const decimal MaxRelativeChange = 0.60m;

    public static GateResult Evaluate(decimal price, string currency, decimal? lastPrice)
    {
        if (price <= 0)
        {
            return new GateResult(PriceObservationDecision.SendToReview, "Price is zero or negative.");
        }

        if (!string.Equals(currency, "EGP", StringComparison.OrdinalIgnoreCase))
        {
            return new GateResult(PriceObservationDecision.SendToReview, $"Currency '{currency}' is not EGP.");
        }

        if (lastPrice is > 0)
        {
            var relativeChange = Math.Abs(price - lastPrice.Value) / lastPrice.Value;
            if (relativeChange > MaxRelativeChange)
            {
                return new GateResult(
                    PriceObservationDecision.SendToReview,
                    $"Price changed by {relativeChange:P0} since the last observation ({lastPrice} -> {price}).");
            }
        }

        return new GateResult(PriceObservationDecision.Accept);
    }
}
