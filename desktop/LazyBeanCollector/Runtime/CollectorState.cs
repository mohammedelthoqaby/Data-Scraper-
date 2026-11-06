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
public sealed class CollectorState
{
    private readonly object _gate = new();
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

    public void SetConnection(ConnectionState state, string detail)
    {
        lock (_gate)
        {
            Connection = state;
            ConnectionDetail = detail;
        }

        Changed?.Invoke();
    }

    public void SetJob(JobProgress? job)
    {
        lock (_gate)
        {
            CurrentJob = job;
        }

        Changed?.Invoke();
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
