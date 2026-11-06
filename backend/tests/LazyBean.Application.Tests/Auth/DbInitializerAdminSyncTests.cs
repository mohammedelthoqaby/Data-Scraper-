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

    [Fact]
    public async Task SeedAdminUserAsync_DoesNotOverwrite_PasswordChangedOnTheWebsite()
    {
        var dbName = Guid.NewGuid().ToString();
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("env-password")));
        await SetPasswordFromWebsiteAsync(CreateServiceProvider(dbName, AdminConfig("env-password")), "website-password");

        // Restart with the same configured value: the website password must survive.
        var restarted = CreateServiceProvider(dbName, AdminConfig("env-password"));
        await DbInitializer.SeedAdminUserAsync(restarted);

        await AssertPasswordAsync(restarted, "website-password", expected: true);
        await AssertPasswordAsync(restarted, "env-password", expected: false);
    }

    [Fact]
    public async Task SeedAdminUserAsync_ResetPassword_AppliesTheConfiguredPassword()
    {
        var dbName = Guid.NewGuid().ToString();
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("old-password")));
        await SetPasswordFromWebsiteAsync(CreateServiceProvider(dbName, AdminConfig("old-password")), "website-password");

        var recovery = CreateServiceProvider(dbName, AdminConfig("recovered-password", reset: true));
        await DbInitializer.SeedAdminUserAsync(recovery);

        await AssertPasswordAsync(recovery, "recovered-password", expected: true);
        await AssertPasswordAsync(recovery, "website-password", expected: false);
    }

    [Fact]
    public async Task SeedAdminUserAsync_ResetPassword_IsAppliedOncePerValue()
    {
        var dbName = Guid.NewGuid().ToString();
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("old-password")));
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("recovered-password", reset: true)));
        await SetPasswordFromWebsiteAsync(CreateServiceProvider(dbName, AdminConfig("recovered-password", reset: true)), "after-recovery-password");

        // The variable is still set on the next restart. The same value must not undo the website change.
        var restarted = CreateServiceProvider(dbName, AdminConfig("recovered-password", reset: true));
        await DbInitializer.SeedAdminUserAsync(restarted);

        await AssertPasswordAsync(restarted, "after-recovery-password", expected: true);
        await AssertPasswordAsync(restarted, "recovered-password", expected: false);
    }

    [Fact]
    public async Task SeedAdminUserAsync_ResetPassword_AppliesAgainForANewValue()
    {
        var dbName = Guid.NewGuid().ToString();
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("old-password")));
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("first-reset", reset: true)));

        var second = CreateServiceProvider(dbName, AdminConfig("second-reset", reset: true));
        await DbInitializer.SeedAdminUserAsync(second);

        await AssertPasswordAsync(second, "second-reset", expected: true);
        await AssertPasswordAsync(second, "first-reset", expected: false);
    }

    [Fact]
    public async Task SeedAdminUserAsync_ResetPassword_EndsExistingSessions()
    {
        var dbName = Guid.NewGuid().ToString();
        var seeded = CreateServiceProvider(dbName, AdminConfig("old-password"));
        await DbInitializer.SeedAdminUserAsync(seeded);
        var stampBefore = await SecurityStampAsync(seeded);

        var recovery = CreateServiceProvider(dbName, AdminConfig("recovered-password", reset: true));
        await DbInitializer.SeedAdminUserAsync(recovery);

        Assert.NotEqual(stampBefore, await SecurityStampAsync(recovery));
    }

    [Fact]
    public async Task SeedAdminUserAsync_ResetPassword_WithoutAPassword_ChangesNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        await DbInitializer.SeedAdminUserAsync(CreateServiceProvider(dbName, AdminConfig("old-password")));

        var misconfigured = CreateServiceProvider(dbName, AdminConfig(password: "", reset: true));
        await DbInitializer.SeedAdminUserAsync(misconfigured);

        await AssertPasswordAsync(misconfigured, "old-password", expected: true);
    }

    private static Dictionary<string, string?> AdminConfig(string password = "initial-secret", bool reset = false)
    {
        var config = new Dictionary<string, string?>
        {
            ["Admin:Username"] = "admin",
            ["Admin:Password"] = password,
        };
        if (reset)
        {
            config["Admin:ResetPassword"] = "true";
        }

        return config;
    }

    // Stands in for a password change made on the website (see ChangePasswordCommandHandler).
    private static async Task SetPasswordFromWebsiteAsync(IServiceProvider sp, string password)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = await db.Users.FirstAsync(u => u.Username == "admin");
        user.PasswordHash = hasher.Hash(password);
        user.PasswordUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task AssertPasswordAsync(IServiceProvider sp, string password, bool expected)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = await db.Users.FirstAsync(u => u.Username == "admin");
        Assert.Equal(expected, hasher.Verify(user.PasswordHash, password));
    }

    private static async Task<string> SecurityStampAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        return (await db.Users.FirstAsync(u => u.Username == "admin")).SecurityStamp;
    }
}
