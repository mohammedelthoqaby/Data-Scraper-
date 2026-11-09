namespace LazyBean.Domain.Rules;

// When an ad counts as running. Google ads are running while they were seen within the window (30 days by default; the
// server's setting can change it). Meta ads carry their own active flag and do not use this rule.
public static class AdActivity
{
    public const int DefaultWindowDays = 30;

    // The earliest last-seen time that still counts as running.
    public static DateTimeOffset CutoffFor(DateTimeOffset now, int windowDays) => now - TimeSpan.FromDays(windowDays);

    // The listing rule: an ad seen on or after the cutoff is running.
    public static bool IsRunning(DateTimeOffset lastSeen, DateTimeOffset cutoff) => lastSeen >= cutoff;

    // The sync rule: an ad last seen on or before the cutoff has ended.
    public static bool IsExpired(DateTimeOffset lastSeen, DateTimeOffset cutoff) => lastSeen <= cutoff;
}
