using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class User : BaseEntity, IAggregateRoot
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // Replaced whenever the password changes. A dashboard cookie carries the stamp it was issued with, so a
    // cookie from before the change (another browser) is refused on its next request.
    public string SecurityStamp { get; set; } = NewSecurityStamp();

    // Set when the password is changed on the website, or by the Admin__ResetPassword recovery at startup. While it
    // is null, startup keeps the stored hash in step with Admin:Password. Once it is set, startup never overwrites it.
    public DateTimeOffset? PasswordUpdatedAt { get; set; }

    // Hash of the Admin:Password value that the last Admin__ResetPassword recovery applied. The recovery runs once per
    // distinct value, so leaving the variable set does not undo a later change made on the website.
    public string? RecoveryMarkerHash { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}
