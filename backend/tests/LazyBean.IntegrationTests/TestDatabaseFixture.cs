using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LazyBean.IntegrationTests;

// Tests must never touch the dev database ("lazybean"). This creates a fresh
// "lazybean_test" database on the same docker-compose Postgres server, migrates it, and
// drops it again when the whole test run finishes - so no test ever truncates or seeds
// data into anything the developer is actually using, and every run starts from a
// genuinely empty schema (no cross-test leftovers to clean up by hand).
public class TestDatabaseFixture : IAsyncLifetime
{
    private const string AdminConnectionString =
        "Host=localhost;Port=15432;Database=postgres;Username=lazybean;Password=changeme";

    private const string TestDatabaseName = "lazybean_test";

    public string ConnectionString { get; } =
        $"Host=localhost;Port=15432;Database={TestDatabaseName};Username=lazybean;Password=changeme";

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using (var drop = admin.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName}";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {TestDatabaseName}";
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new LazyBeanDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using var terminate = admin.CreateCommand();
        // Drop any lingering connections first - EF/Npgsql pooling can otherwise keep the
        // test database open long enough for DROP DATABASE to fail with "in use."
        terminate.CommandText =
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
            $"WHERE datname = '{TestDatabaseName}' AND pid <> pg_backend_pid()";
        await terminate.ExecuteNonQueryAsync();

        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName}";
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>;
