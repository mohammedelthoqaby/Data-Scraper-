using LazyBean.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace LazyBean.Infrastructure.Security;

public class AiKeyProtector : IAiKeyProtector
{
    private readonly IDataProtector _protector;

    public AiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("LazyBean.GoogleAiStudioKey");
    }

    public string EncryptKey(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
            return string.Empty;

        return _protector.Protect(plainKey.Trim());
    }

    public string DecryptKey(string encryptedKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedKey))
            return string.Empty;

        return _protector.Unprotect(encryptedKey);
    }

    public string? MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var trimmed = key.Trim();
        if (trimmed.Length <= 4)
            return new string('•', trimmed.Length);

        var last4 = trimmed[^4..];
        return $"••••••••••••{last4}";
    }
}
