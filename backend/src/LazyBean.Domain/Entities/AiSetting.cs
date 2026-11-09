using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

public class AiSetting : BaseEntity, IAggregateRoot
{
    public bool IsEnabled { get; set; } = false;
    public string? EncryptedApiKey { get; set; }
    public string? KeyLastFour { get; set; }
    public string ModelName { get; set; } = "gemini-3.8-flash";
    public int DailyRequestCap { get; set; } = 1000;
    public int RequestsUsedToday { get; set; } = 0;
    public DateOnly UsageDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateTimeOffset? LastTestedAt { get; set; }
    public bool? LastTestSuccess { get; set; }
    public string? LastTestMessage { get; set; }
    public DateTimeOffset? LastCallAt { get; set; }
    public string? LastCallStatus { get; set; }

    // Gemini reads of Google image previews (AI-read). Off by default; each call counts against its own daily cap.
    public bool CreativeVisionEnabled { get; set; } = false;
    public int CreativeVisionDailyCap { get; set; } = 20;
    public int CreativeVisionUsedToday { get; set; } = 0;
    public DateOnly CreativeVisionUsageDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
