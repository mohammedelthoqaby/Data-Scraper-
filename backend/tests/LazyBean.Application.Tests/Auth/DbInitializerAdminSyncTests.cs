using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.Application.Tests.Auth;

public class DbInitializerAdminSyncTests
{
    private ServiceProvider CreateServiceProvider(string dbName, Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAdminUserAsync_CreatesAdmin_WhenNoUserExists()
    {
        var dbName = Guid.NewGuid().ToString();
        var config = new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin",
            ["Admin:Password"] = "initial-secret"
        };
        var sp = CreateServiceProvider(dbName, config);

        await DbInitializer.SeedAdminUserAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.NotNull(user);
        Assert.True(hasher.Verify(user.PasswordHash, "initial-secret"));
    }

    [Fact]
    public async Task SeedAdminUserAsync_SyncsPassword_WhenConfiguredPasswordChanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var initialConfig = new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin",
            ["Admin:Password"] = "old-password"
        };
        var spInitial = CreateServiceProvider(dbName, initialConfig);

        await DbInitializer.SeedAdminUserAsync(spInitial);

        // Verify initial password
        using (var scope = spInitial.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = await db.Users.FirstAsync(u => u.Username == "admin");
            Assert.True(hasher.Verify(user.PasswordHash, "old-password"));
            Assert.False(hasher.Verify(user.PasswordHash, "new-password-from-env"));
        }

        // Simulate app restart with new Admin:Password configured via environment/user-secrets
        var updatedConfig = new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin",
            ["Admin:Password"] = "new-password-from-env"
        };
        var spUpdated = CreateServiceProvider(dbName, updatedConfig);

        await DbInitializer.SeedAdminUserAsync(spUpdated);

        // Verify password hash was re-hashed and synced
        using (var scope = spUpdated.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = await db.Users.FirstAsync(u => u.Username == "admin");
            Assert.False(hasher.Verify(user.PasswordHash, "old-password"));
            Assert.True(hasher.Verify(user.PasswordHash, "new-password-from-env"));
        }
    }

    [Fact]
    public async Task SeedAdminUserAsync_DoesNothing_WhenPasswordAlreadyMatches()
    {
        var dbName = Guid.NewGuid().ToString();
        var config = new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin",
            ["Admin:Password"] = "same-password"
        };
        var sp = CreateServiceProvider(dbName, config);

        await DbInitializer.SeedAdminUserAsync(sp);

        string existingHash;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            existingHash = (await db.Users.FirstAsync(u => u.Username == "admin")).PasswordHash;
        }

        // Run seed again with exact same config
        await DbInitializer.SeedAdminUserAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
            var currentHash = (await db.Users.FirstAsync(u => u.Username == "admin")).PasswordHash;
            // Hash shouldn't even change if already verified
            Assert.Equal(existingHash, currentHash);
        }
    }
}
