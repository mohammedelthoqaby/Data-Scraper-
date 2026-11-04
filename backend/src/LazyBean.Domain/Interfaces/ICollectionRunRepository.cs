using LazyBean.Domain.Entities;

namespace LazyBean.Domain.Interfaces;

public interface ICollectionRunRepository : IRepository<CollectionRun>
{
    Task<bool> HasActiveRunAsync(CancellationToken ct = default);
    Task<CollectionRun?> GetActiveRunAsync(CancellationToken ct = default);
}
