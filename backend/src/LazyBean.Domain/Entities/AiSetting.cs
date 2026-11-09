using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

/// <summary>
/// Global AI settings (single row). Per-provider keys, models and usage live in
/// <see cref="AiProviderSetting"/> and <see cref="AiProviderUsage"/>.
/// </summary>
public class AiSetting : BaseEntity, IAggregateRoot
{
    public bool IsEnabled { get; set; } = false;

    /// <summary>Provider tried first: "groq" (default) or "gemini" (fallback).</summary>
    public string PrimaryProvider { get; set; } = "groq";

    /// <summary>Safety cap on total AI requests per UTC day across all providers.</summary>
    public int DailyRequestCap { get; set; } = 1000;

    /// <summary>Maximum number of review pairs triaged per run (configurable, default 200).</summary>
    public int TriageRunCap { get; set; } = 200;

    /// <summary>Optional AI-read of Google ad creative images with Gemini (off by default).</summary>
    public bool CreativeVisionEnabled { get; set; } = false;

    /// <summary>Daily cap on creative image reads.</summary>
    public int CreativeVisionDailyCap { get; set; } = 20;

    public int CreativeVisionUsedToday { get; set; } = 0;

    public DateOnly CreativeVisionUsageDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
