using LazyBean.Application.Collection.Validation;
using LazyBean.Domain.Enums;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class ProductRemovalConfirmationTests
{
    [Fact]
    public void EvaluateMissing_ActiveProduct_TransitionsToMissingOnceWithNoChangeEvent()
    {
        var (newStatus, emitChangeEvent) = ProductRemovalConfirmation.EvaluateMissing(ProductStatus.Active);

        Assert.Equal(ProductStatus.MissingOnce, newStatus);
        Assert.False(emitChangeEvent, "First miss must NOT emit a ProductRemoved ChangeEvent.");
    }

    [Fact]
    public void EvaluateMissing_MissingOnceProduct_TransitionsToRemovedAndEmitsChangeEvent()
    {
        var (newStatus, emitChangeEvent) = ProductRemovalConfirmation.EvaluateMissing(ProductStatus.MissingOnce);

        Assert.Equal(ProductStatus.Removed, newStatus);
        Assert.True(emitChangeEvent, "Second consecutive miss MUST emit a ProductRemoved ChangeEvent.");
    }

    [Fact]
    public void EvaluateMissing_AlreadyRemovedProduct_StaysRemovedWithNoDuplicateChangeEvent()
    {
        var (newStatus, emitChangeEvent) = ProductRemovalConfirmation.EvaluateMissing(ProductStatus.Removed);

        Assert.Equal(ProductStatus.Removed, newStatus);
        Assert.False(emitChangeEvent, "Already-removed products must not emit repeated ChangeEvents.");
    }
}
