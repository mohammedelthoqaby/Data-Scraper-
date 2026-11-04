using LazyBean.Domain.Entities;

namespace LazyBean.Domain.Interfaces;

// Extends the generic repository with queries specific to Competitor.
public interface ICompetitorRepository : IRepository<Competitor>
{
    Task<Competitor?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<bool> KeyExistsAsync(string key, CancellationToken ct = default);
}
