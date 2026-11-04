using LazyBean.Application.Collection.Contracts;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Orchestration;

// One adapter per Platform (see ICompetitorAdapter); this just picks the right one.
public interface ICompetitorAdapterResolver
{
    ICompetitorAdapter Resolve(CompetitorPlatform platform);
}
