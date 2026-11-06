using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Migration adding CollectionHost to competitors and LastCallAt/LastCallStatus to ai_settings.
    /// Uses idempotent DDL (IF NOT EXISTS via migrationBuilder.Sql) to be fully safe across:
    /// 1. Fresh databases.
    /// 2. Databases migrated sequentially from 20261105122306.
    /// 3. Databases where the legacy hand-written migration 20261105183500 was previously recorded.
    /// Automatically reconciles __EFMigrationsHistory by purging the obsolete 20261105183500 entry.
    /// </summary>
    public partial class AddCollectionHostAndAiCallStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'competitors' AND column_name = 'CollectionHost'
                    ) THEN
                        ALTER TABLE competitors ADD COLUMN ""CollectionHost"" character varying(32) NOT NULL DEFAULT 'server';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'ai_settings' AND column_name = 'LastCallAt'
                    ) THEN
                        ALTER TABLE ai_settings ADD COLUMN ""LastCallAt"" timestamp with time zone NULL;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'ai_settings' AND column_name = 'LastCallStatus'
                    ) THEN
                        ALTER TABLE ai_settings ADD COLUMN ""LastCallStatus"" character varying(100) NULL;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'
                    ) THEN
                        DELETE FROM ""__EFMigrationsHistory"" 
                        WHERE ""MigrationId"" = '20261105183500_AddCollectionHostAndAiCallStatus';
                    END IF;
                END $$;
            ");
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
