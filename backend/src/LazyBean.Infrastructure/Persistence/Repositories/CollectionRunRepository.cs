using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Infrastructure.Persistence.Repositories;

public class CollectionRunRepository(LazyBeanDbContext context) : Repository<CollectionRun>(context), ICollectionRunRepository
{
    public Task<bool> HasActiveRunAsync(CancellationToken ct = default) =>
        DbSet.AnyAsync(
            r => r.Status == CollectionRunStatus.Queued || r.Status == CollectionRunStatus.Running, ct);

    public Task<CollectionRun?> GetActiveRunAsync(CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(
            r => r.Status == CollectionRunStatus.Queued || r.Status == CollectionRunStatus.Running, ct);
}
