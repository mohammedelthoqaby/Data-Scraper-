using MediatR;

namespace LazyBean.Application.Auth.Commands.ChangePassword;

// Changes the signed-in user's dashboard password. RevokeDevices also signs out every paired collector app; by default
// they keep working, because they use their own device tokens.
public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword, bool RevokeDevices)
    : IRequest<ChangePasswordResult>;

// SecurityStamp is the stamp the caller signs the current session in with, so only this browser stays signed in.
public sealed record ChangePasswordResult(string SecurityStamp, DateTimeOffset PasswordChangedAt, int DevicesRevoked);
