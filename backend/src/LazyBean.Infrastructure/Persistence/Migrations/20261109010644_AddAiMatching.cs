using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBundle",
                table: "product_matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "product_matches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_match_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompetitorVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GuardrailReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_match_decisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_match_decisions_CompetitorVariantId_ReferenceVariantId",
                table: "ai_match_decisions",
                columns: new[] { "CompetitorVariantId", "ReferenceVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_match_decisions_PairHash",
                table: "ai_match_decisions",
                column: "PairHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_match_decisions");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "products");

            migrationBuilder.DropColumn(
                name: "IsBundle",
                table: "product_matches");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "product_matches");
        }
    }
}
