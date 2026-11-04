using LazyBean.Domain.Common;

namespace LazyBean.Application.Common.Interfaces;

// Read side of CQRS: queries get a no-tracking IQueryable to project straight into DTOs.
// Commands never use this - they go through IRepository<T> + IUnitOfWork so changes are tracked.
public interface IReadDbContext
{
    IQueryable<T> Query<T>() where T : BaseEntity;
}
