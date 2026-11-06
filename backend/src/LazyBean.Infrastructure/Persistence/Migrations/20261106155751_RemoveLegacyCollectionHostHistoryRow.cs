using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Removes the obsolete history row for the legacy hand-written migration 20261105183500, which
    /// had the same content as 20261105173443. The schema is already correct at this point (the
    /// columns are added idempotently by 20261105173443), so only the history row changes.
    /// Runs once on every database that has not yet recorded it. A DELETE that matches nothing is a
    /// no-op, so fresh databases, databases at 20261105122306, and databases that already have
    /// 20261105173443 all pass through safely. Down is intentionally empty: the legacy row is
    /// never restored.
    /// </summary>
    public partial class RemoveLegacyCollectionHostHistoryRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DELETE FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20261105183500_AddCollectionHostAndAiCallStatus';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
