using System.Threading.Channels;
using LazyBean.Application.Collection.Orchestration;

namespace LazyBean.Infrastructure.Collection.Orchestration;

// Unbounded is fine here: a run id is a few bytes and StartRunCommand already refuses to
// enqueue a second run while one is queued/running (see CollectionRunsGate).
public class ChannelRunQueue : IRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(runId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
