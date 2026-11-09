using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedApiKey",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "KeyLastFour",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastCallAt",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastCallStatus",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastTestMessage",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastTestSuccess",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "RequestsUsedToday",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "UsageDate",
                table: "ai_settings");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryProvider",
                table: "ai_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "groq");

            migrationBuilder.AddColumn<int>(
                name: "TriageRunCap",
                table: "ai_settings",
                type: "integer",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.CreateTable(
                name: "ai_provider_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    KeyPrefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    KeyLastFour = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastTestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastErrorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Last429At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_provider_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UsageDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_usages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_provider_settings_Provider",
                table: "ai_provider_settings",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_provider_usages_Provider_UsageDate",
                table: "ai_provider_usages",
                columns: new[] { "Provider", "UsageDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_provider_settings");

            migrationBuilder.DropTable(
                name: "ai_provider_usages");

            migrationBuilder.DropColumn(
                name: "PrimaryProvider",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "TriageRunCap",
                table: "ai_settings");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedApiKey",
                table: "ai_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyLastFour",
                table: "ai_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

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

            migrationBuilder.AddColumn<string>(
                name: "LastTestMessage",
                table: "ai_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastTestSuccess",
                table: "ai_settings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "ai_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "ai_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RequestsUsedToday",
                table: "ai_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "UsageDate",
                table: "ai_settings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }
    }
}
