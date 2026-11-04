using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace LazyBean.Infrastructure.Security;

// Wraps ASP.NET Core Identity's PasswordHasher<T> (PBKDF2, salted, versioned) without
// pulling in the rest of Identity (stores, UI, sign-in manager) - just the hashing algorithm.
public class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(default!, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(default!, hash, password) != PasswordVerificationResult.Failed;
}
