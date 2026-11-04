using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Infrastructure.Persistence.Repositories;

public class CompetitorRepository(LazyBeanDbContext context)
    : Repository<Competitor>(context), ICompetitorRepository
{
    public Task<Competitor?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(c => c.Key == key, ct);

    public Task<bool> KeyExistsAsync(string key, CancellationToken ct = default) =>
        DbSet.AnyAsync(c => c.Key == key, ct);
}
