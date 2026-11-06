using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.Runtime;

public enum ConnectionState
{
    Starting,
    Connected,
    Waiting,
    NotPaired,
    PairingRequired,
    Stopped,
}

public sealed record JobProgress(string Title, string Message, int Done, int? Total, DateTimeOffset StartedAt);

// Where the running job sits in its batch: the place (1-based) and the batch's size.
public sealed record BatchProgress(int Position, int Total);

// What the window shows. Background threads update it and raise Changed; the UI marshals to its own thread.
public sealed class CollectorState(Func<DateTimeOffset>? clock = null)
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset? _waitingSince;
    private readonly Dictionary<string, SourceSync> _lastSync = new(StringComparer.Ordinal);

    public event Action? Changed;

    public event Action<IReadOnlyDictionary<string, SourceSync>>? SyncRecorded;

    public ConnectionState Connection { get; private set; } = ConnectionState.Starting;

    public string ConnectionDetail { get; private set; } = "Starting…";

    public JobProgress? CurrentJob { get; private set; }

    // The running job's batch, or null when nothing is running.
    public BatchProgress? Batch { get; private set; }

    public void LoadLastSync(IReadOnlyDictionary<string, SourceSync> saved)
    {
        lock (_gate)
        {
            _lastSync.Clear();
            foreach (var (key, value) in saved)
            {
                _lastSync[key] = value;
            }
        }
    }

    public IReadOnlyDictionary<string, SourceSync> LastSync()
    {
        lock (_gate)
        {
            return new Dictionary<string, SourceSync>(_lastSync, StringComparer.Ordinal);
        }
    }

    // How long the server has not answered, counted from the first failed claim. Null while it answers.
    public TimeSpan? UnreachableFor
    {
        get
        {
            lock (_gate)
            {
                return _waitingSince is { } since ? _clock() - since : null;
            }
        }
    }

    public void SetConnection(ConnectionState state, string detail)
    {
        lock (_gate)
        {
            Connection = state;
            ConnectionDetail = detail;
            if (state == ConnectionState.Waiting)
            {
                _waitingSince ??= _clock();
            }
            else
            {
                _waitingSince = null;
            }
        }

        Changed?.Invoke();
    }

    public void SetJob(JobProgress? job)
    {
        lock (_gate)
        {
            CurrentJob = job;
            if (job is not null)
            {
                IsAllDone = false;
            }
        }

        Changed?.Invoke();
    }

    // Raised once, when this PC has no more work for what it just did. Cleared when new work starts.
    public event Action? AllDone;

    public bool IsAllDone { get; private set; }

    public void MarkAllDone()
    {
        lock (_gate)
        {
            if (IsAllDone)
            {
                return;
            }

            IsAllDone = true;
        }

        Changed?.Invoke();
        AllDone?.Invoke();
    }

    public void SetBatch(BatchProgress? batch)
    {
        lock (_gate)
        {
            Batch = batch;
        }

        Changed?.Invoke();
    }

    public void UpdateJob(string message, int done, int? total)
    {
        lock (_gate)
        {
            if (CurrentJob is null)
            {
                return;
            }

            CurrentJob = CurrentJob with { Message = message, Done = done, Total = total };
        }

        Changed?.Invoke();
    }

    public void RecordSync(string source, string result)
    {
        IReadOnlyDictionary<string, SourceSync> snapshot;
        lock (_gate)
        {
            _lastSync[source] = new SourceSync(DateTimeOffset.Now, result);
            snapshot = new Dictionary<string, SourceSync>(_lastSync, StringComparer.Ordinal);
        }

        SyncRecorded?.Invoke(snapshot);
        Changed?.Invoke();
    }
}
