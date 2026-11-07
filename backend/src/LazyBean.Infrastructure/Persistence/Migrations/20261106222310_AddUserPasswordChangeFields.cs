using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordChangeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordUpdatedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryMarkerHash",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Existing users get a real stamp. An empty stamp would refuse every dashboard session at its next request.
            migrationBuilder.Sql(
                """UPDATE users SET "SecurityStamp" = md5(random()::text || clock_timestamp()::text || "Id"::text) WHERE "SecurityStamp" = '';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordUpdatedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RecoveryMarkerHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "users");
        }
    }
}
