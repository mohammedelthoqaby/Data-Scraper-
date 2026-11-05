namespace LazyBean.Application.Collection.Orchestration;

public interface IRunCancellationTokenRegistry
{
    CancellationToken Register(Guid runId, CancellationToken parentToken);
    bool TryCancel(Guid runId);
    void Unregister(Guid runId);
}
