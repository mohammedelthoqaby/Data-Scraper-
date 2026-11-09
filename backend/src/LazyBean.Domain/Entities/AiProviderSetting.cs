using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

/// <summary>
/// Per-provider configuration. One row per provider ("groq", "gemini").
/// The API key is stored only encrypted (ASP.NET Data Protection); the masked
/// fields are the only key-derived values ever returned to a client.
/// </summary>
public class AiProviderSetting : BaseEntity, IAggregateRoot
{
    public string Provider { get; set; } = string.Empty;
    public string? EncryptedApiKey { get; set; }
    public string? KeyPrefix { get; set; }
    public string? KeyLastFour { get; set; }
    public string? ModelName { get; set; }

    public DateTimeOffset? LastTestedAt { get; set; }
    public string? LastTestStatus { get; set; }
    public string? LastTestMessage { get; set; }

    public DateTimeOffset? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? Last429At { get; set; }
}
