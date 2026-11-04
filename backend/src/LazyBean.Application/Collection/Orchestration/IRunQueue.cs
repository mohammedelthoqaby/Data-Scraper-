namespace LazyBean.Application.Collection.Orchestration;

// Only one run executes at a time - StartRunCommand enqueues, a single BackgroundService
// drains this one run at a time (see Infrastructure's Channel-backed implementation).
public interface IRunQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}
