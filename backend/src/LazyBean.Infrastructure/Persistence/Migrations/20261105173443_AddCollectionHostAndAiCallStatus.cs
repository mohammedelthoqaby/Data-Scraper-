using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
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
