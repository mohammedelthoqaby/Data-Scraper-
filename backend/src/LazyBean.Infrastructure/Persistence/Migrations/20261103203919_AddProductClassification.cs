using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalCategory",
                table: "products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ClassificationConfidence",
                table: "products",
                type: "numeric(4,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchedRule",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductType",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "products",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanonicalCategory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ClassificationConfidence",
                table: "products");

            migrationBuilder.DropColumn(
                name: "MatchedRule",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ProductType",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Vendor",
                table: "products");
        }
    }
}
