using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreativeDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ads",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DetailFetchedAt",
                table: "ads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DetailLastShownAt",
                table: "ads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Headline",
                table: "ads",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewVariationsJson",
                table: "ads",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "DetailFetchedAt",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "DetailLastShownAt",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "Headline",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "PreviewVariationsJson",
                table: "ads");
        }
    }
}
