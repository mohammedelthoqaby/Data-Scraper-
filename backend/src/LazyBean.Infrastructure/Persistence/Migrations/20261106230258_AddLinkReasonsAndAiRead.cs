using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkReasonsAndAiRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreativeVisionDailyCap",
                table: "ai_settings",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<bool>(
                name: "CreativeVisionEnabled",
                table: "ai_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CreativeVisionUsageDate",
                table: "ai_settings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "CreativeVisionUsedToday",
                table: "ai_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiReadAt",
                table: "ads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiReadName",
                table: "ads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiReadPrice",
                table: "ads",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkReason",
                table: "ads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreativeVisionDailyCap",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "CreativeVisionEnabled",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "CreativeVisionUsageDate",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "CreativeVisionUsedToday",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "AiReadAt",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "AiReadName",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "AiReadPrice",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "LinkReason",
                table: "ads");
        }
    }
}
