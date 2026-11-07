using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace LazyBean.IntegrationTests;

// Every state a database can be in when it upgrades to this release must reach the same schema and the
// same history. Each case gets its own throwaway database, dropped at the end, so the shared test
// database is never touched.
public class MigrationHistoryTests
{
    private const string CollectionRunPhases = "20261105122306_AddPhasesAndMatchesToCollectionRun";
    private const string CollectionHost = "20261105173443_AddCollectionHostAndAiCallStatus";
    private const string LegacyHistoryRow = "20261105183500_AddCollectionHostAndAiCallStatus";

    private static string AdminConnectionString =>
        $"Host=localhost;Port=15432;Database=postgres;Username=lazybean;Password={TestDb.Password}";

    [Theory]
    [InlineData("fresh")]
    [InlineData("at_collection_run_phases")]
    [InlineData("legacy_history_row")]
    [InlineData("applied_through_collection_host")]
    public async Task Upgrade_ReachesTheLatestSchema_FromEveryKnownState(string state)
    {
        var databaseName = $"lazybean_migration_{state}";
        await CreateDatabaseAsync(databaseName);
        try
        {
            var connectionString = DatabaseConnectionString(databaseName);
            var options = new DbContextOptionsBuilder<LazyBeanDbContext>().UseNpgsql(connectionString).Options;
            await using var db = new LazyBeanDbContext(options);

            await ArrangeStateAsync(db, state);
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.DoesNotContain(LegacyHistoryRow, await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains(CollectionHost, await db.Database.GetAppliedMigrationsAsync());
            Assert.True(await ColumnExistsAsync(connectionString, "competitors", "CollectionHost"));
            Assert.True(await ColumnExistsAsync(connectionString, "ai_settings", "LastCallAt"));
            Assert.True(await ColumnExistsAsync(connectionString, "ai_settings", "LastCallStatus"));
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static async Task ArrangeStateAsync(LazyBeanDbContext db, string state)
    {
        var migrator = db.GetService<IMigrator>();

        switch (state)
        {
            case "fresh":
                return;

            case "at_collection_run_phases":
                await migrator.MigrateAsync(CollectionRunPhases);
                return;

            case "legacy_history_row":
                // The legacy hand-written migration added the same columns and recorded its own row.
                await migrator.MigrateAsync(CollectionRunPhases);
                await db.Database.ExecuteSqlRawAsync(
                    @"ALTER TABLE competitors ADD COLUMN ""CollectionHost"" character varying(32) NOT NULL DEFAULT 'server';");
                await db.Database.ExecuteSqlRawAsync(
                    @"ALTER TABLE ai_settings ADD COLUMN ""LastCallAt"" timestamp with time zone NULL;");
                await db.Database.ExecuteSqlRawAsync(
                    @"ALTER TABLE ai_settings ADD COLUMN ""LastCallStatus"" character varying(100) NULL;");
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20261105183500_AddCollectionHostAndAiCallStatus', '10.0.0');");
                return;

            case "applied_through_collection_host":
                // Recorded by an earlier build of this release, with the legacy row still present beside it.
                await migrator.MigrateAsync(CollectionHost);
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20261105183500_AddCollectionHostAndAiCallStatus', '10.0.0');");
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown database state.");
        }
    }

    private static string DatabaseConnectionString(string databaseName) =>
        $"Host=localhost;Port=15432;Database={databaseName};Username=lazybean;Password={TestDb.Password}";

    private static async Task<bool> ColumnExistsAsync(string connectionString, string table, string column)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @table AND column_name = @column";
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using (var drop = admin.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {databaseName}";
            await create.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropDatabaseAsync(string databaseName)
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using (var terminate = admin.CreateCommand())
        {
            terminate.CommandText =
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()";
            await terminate.ExecuteNonQueryAsync();
        }

        await using (var drop = admin.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
