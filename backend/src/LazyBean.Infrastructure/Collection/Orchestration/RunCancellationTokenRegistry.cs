using System.Collections.Concurrent;
using LazyBean.Application.Collection.Orchestration;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class RunCancellationTokenRegistry : IRunCancellationTokenRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public CancellationToken Register(Guid runId, CancellationToken parentToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _sources[runId] = cts;
        return cts.Token;
    }

    public bool TryCancel(Guid runId)
    {
        if (_sources.TryGetValue(runId, out var cts))
        {
            try
            {
                cts.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
        return false;
    }

    public void Unregister(Guid runId)
    {
        if (_sources.TryRemove(runId, out var cts))
        {
            try
            {
                cts.Dispose();
            }
            catch
            {
            }
        }
    }
}
