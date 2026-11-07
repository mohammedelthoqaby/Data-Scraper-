using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Adds CollectionHost to competitors and LastCallAt/LastCallStatus to ai_settings.
    /// The column adds use ADD COLUMN IF NOT EXISTS, so this is safe on a fresh database, on a
    /// database at 20261105122306, and on a database where the legacy hand-written migration
    /// 20261105183500 already created these columns. The obsolete 20261105183500 history row is
    /// removed by the later migration RemoveLegacyCollectionHostHistoryRow.
    /// </summary>
    public partial class AddCollectionHostAndAiCallStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE competitors ADD COLUMN IF NOT EXISTS ""CollectionHost"" character varying(32) NOT NULL DEFAULT 'server';");
            migrationBuilder.Sql(@"ALTER TABLE ai_settings ADD COLUMN IF NOT EXISTS ""LastCallAt"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ai_settings ADD COLUMN IF NOT EXISTS ""LastCallStatus"" character varying(100) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectionHost",
                table: "competitors");

            migrationBuilder.DropColumn(
                name: "LastCallAt",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastCallStatus",
                table: "ai_settings");
        }
    }
}
