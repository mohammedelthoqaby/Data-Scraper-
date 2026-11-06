using System.Collections.Concurrent;
using LazyBean.Application.Collection.Orchestration;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class RunCancellationTokenRegistry : IRunCancellationTokenRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();
    private readonly ConcurrentDictionary<Guid, bool> _userCancelled = new();

    public CancellationToken Register(Guid runId, CancellationToken parentToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _sources[runId] = cts;
        return cts.Token;
    }

    public bool TryCancel(Guid runId)
    {
        _userCancelled[runId] = true;
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

    public bool IsUserCancelled(Guid runId)
    {
        return _userCancelled.TryGetValue(runId, out var val) && val;
    }

    public void MarkUserCancelled(Guid runId)
    {
        _userCancelled[runId] = true;
    }

    public void Unregister(Guid runId)
    {
        _userCancelled.TryRemove(runId, out _);
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
