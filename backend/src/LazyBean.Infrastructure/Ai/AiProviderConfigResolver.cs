using LazyBean.Application.Ai;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Ai;

/// <summary>
/// Resolves where a provider's key and model come from. An environment variable
/// (Ai:Groq:ApiKey / Ai:Gemini:ApiKey, i.e. Ai__Groq__ApiKey) always wins over the saved key.
/// </summary>
internal static class AiProviderConfigResolver
{
    public const string SourceEnvironment = "environment";
    public const string SourceDatabase = "database";
    public const string SourceNone = "none";

    public static string KeySource(AiOptions options, string provider, AiProviderSetting? row)
    {
        if (!string.IsNullOrWhiteSpace(options.ForProvider(provider).ApiKey))
            return SourceEnvironment;
        if (!string.IsNullOrWhiteSpace(row?.EncryptedApiKey))
            return SourceDatabase;
        return SourceNone;
    }

    public static string ModelFor(string provider, AiProviderSetting? row) =>
        !string.IsNullOrWhiteSpace(row?.ModelName) ? row.ModelName.Trim() : AiProviders.DefaultModel(provider);

    /// <summary>Returns the plaintext key for server-side calls only. Never log or return it.</summary>
    public static string? ResolveKey(
        string provider,
        AiOptions options,
        AiProviderSetting? row,
        IAiKeyProtector protector,
        ILogger logger)
    {
        var fromEnvironment = options.ForProvider(provider).ApiKey;
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        if (string.IsNullOrWhiteSpace(row?.EncryptedApiKey))
            return null;

        try
        {
            var key = protector.DecryptKey(row.EncryptedApiKey);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (Exception)
        {
            // The key ring changed (for example a new App_Data/keys folder). Re-entering the key fixes it.
            logger.LogWarning("The saved {Provider} API key cannot be decrypted with the current Data Protection key ring. Re-enter it in AI settings.", provider);
            return null;
        }
    }
}
