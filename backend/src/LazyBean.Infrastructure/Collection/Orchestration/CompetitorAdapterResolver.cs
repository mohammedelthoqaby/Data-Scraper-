using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Enums;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class CompetitorAdapterResolver(IEnumerable<ICompetitorAdapter> adapters) : ICompetitorAdapterResolver
{
    public ICompetitorAdapter Resolve(CompetitorPlatform platform) =>
        adapters.FirstOrDefault(a => a.Platform == platform)
        ?? throw new NotSupportedException($"No adapter registered for platform '{platform}'.");
}
