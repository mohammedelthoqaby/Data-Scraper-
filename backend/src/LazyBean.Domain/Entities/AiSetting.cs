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
}
