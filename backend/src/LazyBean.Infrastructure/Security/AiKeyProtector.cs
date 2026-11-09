using LazyBean.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace LazyBean.Infrastructure.Security;

/// <summary>
/// Encrypts provider API keys with ASP.NET Core Data Protection and produces the only
/// key-derived text that may leave the server: a masked form such as "gsk_••••1234".
/// </summary>
public class AiKeyProtector : IAiKeyProtector
{
    private const string Purpose = "LazyBean.AiProviderApiKey";
    private const int MinLengthForPrefix = 12;
    private const int PrefixLength = 4;

    private readonly IDataProtector _protector;

    public AiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
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

        var prefix = trimmed.Length >= MinLengthForPrefix ? trimmed[..PrefixLength] : string.Empty;
        return $"{prefix}••••{trimmed[^4..]}";
    }

    /// <summary>Builds the masked form from parts stored at save time (prefix and last four only).</summary>
    public static string BuildMask(string? prefix, string? lastFour)
    {
        if (string.IsNullOrWhiteSpace(lastFour))
            return string.Empty;

        return $"{prefix ?? string.Empty}••••{lastFour}";
    }
}
