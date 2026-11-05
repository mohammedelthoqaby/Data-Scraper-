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
            migrationBuilder.AddColumn<string>(
                name: "CollectionHost",
                table: "competitors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "server");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCallAt",
                table: "ai_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCallStatus",
                table: "ai_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
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
