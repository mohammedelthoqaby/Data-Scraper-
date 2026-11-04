using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Infrastructure.Persistence.Repositories;

public class UserRepository(LazyBeanDbContext context) : Repository<User>(context), IUserRepository
{
    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        DbSet.FirstOrDefaultAsync(u => u.Username == username, ct);
}
