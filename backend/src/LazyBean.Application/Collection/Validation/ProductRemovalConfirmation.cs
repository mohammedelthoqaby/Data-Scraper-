using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Validation;

// Removal confirmation policy:
// A product is only confirmed Removed when it has been missing in 2 consecutive Completed runs.
// - On the first miss (Active): transition to MissingOnce, with NO ChangeEvent emitted.
// - On the second consecutive miss (MissingOnce): transition to Removed, and emit ProductRemoved ChangeEvent.
// - If a product reappears in a later run: transition back to Active.
public static class ProductRemovalConfirmation
{
    public static (ProductStatus NewStatus, bool EmitChangeEvent) EvaluateMissing(ProductStatus currentStatus)
    {
        return currentStatus switch
        {
            ProductStatus.Active => (ProductStatus.MissingOnce, false),
            ProductStatus.MissingOnce => (ProductStatus.Removed, true),
            _ => (currentStatus, false)
        };
    }
}
