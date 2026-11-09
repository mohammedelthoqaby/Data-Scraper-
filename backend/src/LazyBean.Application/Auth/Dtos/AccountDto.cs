namespace LazyBean.Application.Auth.Dtos;

public sealed record AccountDto(Guid Id, string Username, DateTimeOffset? PasswordChangedAt);
