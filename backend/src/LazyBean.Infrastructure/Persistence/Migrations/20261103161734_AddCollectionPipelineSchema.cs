using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionPipelineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_price_observations_RunId",
                table: "price_observations");

            migrationBuilder.RenameColumn(
                name: "WeightG",
                table: "variants",
                newName: "WeightGrams");

            migrationBuilder.RenameColumn(
                name: "WeightG",
                table: "products",
                newName: "WeightGrams");

            migrationBuilder.AddColumn<int>(
                name: "UnitCount",
                table: "variants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrency",
                table: "competitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinDelayMs",
                table: "competitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "fetched_resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fetched_resources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_VariantId_RunId",
                table: "price_observations",
                columns: new[] { "VariantId", "RunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fetched_resources_CompetitorId",
                table: "fetched_resources",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_fetched_resources_RunId",
                table: "fetched_resources",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fetched_resources");

            migrationBuilder.DropIndex(
                name: "IX_price_observations_VariantId_RunId",
                table: "price_observations");

            migrationBuilder.DropColumn(
                name: "UnitCount",
                table: "variants");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "products");

            migrationBuilder.DropColumn(
                name: "MaxConcurrency",
                table: "competitors");

            migrationBuilder.DropColumn(
                name: "MinDelayMs",
                table: "competitors");

            migrationBuilder.RenameColumn(
                name: "WeightGrams",
                table: "variants",
                newName: "WeightG");

            migrationBuilder.RenameColumn(
                name: "WeightGrams",
                table: "products",
                newName: "WeightG");

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_RunId",
                table: "price_observations",
                column: "RunId");
        }
    }
}
