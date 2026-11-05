using LazyBean.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LazyBean.IntegrationTests;

public class PgBouncerVerificationTests
{
    private const string AdminConnectionString =
        "Host=localhost;Port=15432;Database=postgres;Username=lazybean;Password=changeme";

    private const string PgBouncerBaseConnectionString =
        "Host=localhost;Port=16432;Username=lazybean;Password=changeme;No Reset On Close=true";

    [Fact]
    public async Task PgBouncer_DirectConnection_ExecutesQueryInTransactionMode()
    {
        var connStr = $"{PgBouncerBaseConnectionString};Database=lazybean";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_database(), current_user, 1 + 1;";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("lazybean", reader.GetString(0));
        Assert.Equal("lazybean", reader.GetString(1));
        Assert.Equal(2, reader.GetInt32(2));
    }

    [Fact]
    public async Task PgBouncer_EmptyDatabase_MigrationsApplyCleanly()
    {
        const string emptyDbName = "lazybean_empty_migrate_test";

        // Create empty DB directly in postgres
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {emptyDbName}";
            await drop.ExecuteNonQueryAsync();

            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {emptyDbName}";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            // Point EF Core context through PgBouncer in transaction pooling mode
            var pgbouncerConnStr = $"{PgBouncerBaseConnectionString};Database={emptyDbName}";
            var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
                .UseNpgsql(pgbouncerConnStr)
                .Options;

            await using (var db = new LazyBeanDbContext(options))
            {
                // Run EF Core Migrations
                await db.Database.MigrateAsync();

                // Verify tables exist
                var canConnect = await db.Database.CanConnectAsync();
                Assert.True(canConnect);

                var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
                Assert.NotEmpty(appliedMigrations);
            }
        }
        finally
        {
            // Clean up empty DB
            await using var admin = new NpgsqlConnection(AdminConnectionString);
            await admin.OpenAsync();
            await using var term = admin.CreateCommand();
            term.CommandText = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{emptyDbName}' AND pid <> pg_backend_pid()";
            await term.ExecuteNonQueryAsync();

            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {emptyDbName}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task PgBouncer_DataProtectionKeyRing_PersistsAndSurvives()
    {
        var pgbouncerConnStr = $"{PgBouncerBaseConnectionString};Database=lazybean";

        var services = new ServiceCollection();
        services.AddDbContext<LazyBeanDbContext>(opts => opts.UseNpgsql(pgbouncerConnStr));
        services.AddDataProtection()
            .SetApplicationName("LazyBean")
            .PersistKeysToDbContext<LazyBeanDbContext>();

        var sp = services.BuildServiceProvider();
        var dataProtector = sp.GetDataProtector("PgBouncerTestPurpose");

        var secret = "TestSecretData-MonsterAspPgBouncer";
        var encrypted = dataProtector.Protect(secret);
        var decrypted = dataProtector.Unprotect(encrypted);

        Assert.Equal(secret, decrypted);

        // Verify key exists in database table
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LazyBeanDbContext>();
        var keys = await db.DataProtectionKeys.ToListAsync();
        Assert.NotEmpty(keys);
    }
}
