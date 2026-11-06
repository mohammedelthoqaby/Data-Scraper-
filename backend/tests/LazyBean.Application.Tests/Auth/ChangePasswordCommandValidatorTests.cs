using LazyBean.Application.Auth.Commands.ChangePassword;
using Xunit;

namespace LazyBean.Application.Tests.Auth;

public class ChangePasswordCommandValidatorTests
{
    private static readonly ChangePasswordCommandValidator Validator = new();

    [Fact]
    public void NewPassword_OfTenCharacters_IsAccepted()
    {
        var result = Validator.Validate(Command(newPassword: "abcdefghij"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NewPassword_ShorterThanTenCharacters_IsRefused()
    {
        var result = Validator.Validate(Command(newPassword: "abcdefghi"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword)
            && e.ErrorMessage.Contains("at least 10 characters"));
    }

    [Fact]
    public void NewPassword_SameAsTheCurrentOne_IsRefused()
    {
        var result = Validator.Validate(Command(currentPassword: "same-password-1", newPassword: "same-password-1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void CurrentPassword_Missing_IsRefused()
    {
        var result = Validator.Validate(Command(currentPassword: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }

    private static ChangePasswordCommand Command(
        string currentPassword = "current-password-1",
        string newPassword = "a-new-password-2") =>
        new(Guid.NewGuid(), currentPassword, newPassword, RevokeDevices: false);
}
