using System.Linq.Expressions;
using LazyBean.Domain.Common;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Infrastructure.Persistence.Repositories;

// Generic EF Core repository shared by every aggregate. Specific repositories
// (e.g. CompetitorRepository) derive from this and add query methods of their own.
public class Repository<T>(LazyBeanDbContext context) : IRepository<T>
    where T : BaseEntity, IAggregateRoot
{
    protected readonly LazyBeanDbContext Context = context;
    protected readonly DbSet<T> DbSet = context.Set<T>();

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await DbSet.FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await DbSet.AsNoTracking().ToListAsync(ct);

    public virtual async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await DbSet.AsNoTracking().Where(predicate).ToListAsync(ct);

    public virtual async Task AddAsync(T entity, CancellationToken ct = default) =>
        await DbSet.AddAsync(entity, ct);

    public virtual void Update(T entity) => DbSet.Update(entity);

    public virtual void Remove(T entity) => DbSet.Remove(entity);
}
