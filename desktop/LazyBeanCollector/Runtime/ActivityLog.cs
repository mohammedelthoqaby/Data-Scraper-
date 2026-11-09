namespace LazyBean.Collector.App.Runtime;

public sealed record LogEntry(DateTimeOffset At, string Level, string Message);

// The log the window shows, plus a daily file under %LOCALAPPDATA%\LazyBeanCollector\logs for support.
// Logging never throws: a full disk must not stop collection.
public sealed class ActivityLog(string logsDirectory) : LazyBean.Application.Collector.Ports.ICollectionLog
{
    private const int MaxInMemory = 500;

    private readonly object _gate = new();
    private readonly LinkedList<LogEntry> _entries = new();

    public event Action<LogEntry>? Added;

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxInMemory)
            {
                _entries.RemoveFirst();
            }

            try
            {
                Directory.CreateDirectory(logsDirectory);
                var file = Path.Combine(logsDirectory, $"{entry.At:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{entry.At:HH:mm:ss} {level,-5} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // The window still shows the entry; the file is best effort.
            }
        }

        Added?.Invoke(entry);
    }
}
