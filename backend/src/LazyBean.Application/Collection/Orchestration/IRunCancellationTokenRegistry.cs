namespace LazyBean.Application.Collection.Orchestration;

public interface IRunCancellationTokenRegistry
{
    CancellationToken Register(Guid runId, CancellationToken parentToken);
    bool TryCancel(Guid runId);
    bool IsUserCancelled(Guid runId);
    void MarkUserCancelled(Guid runId);
    void Unregister(Guid runId);
}
