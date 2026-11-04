using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class User : BaseEntity, IAggregateRoot
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
