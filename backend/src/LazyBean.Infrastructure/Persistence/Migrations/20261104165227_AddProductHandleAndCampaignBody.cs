using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductHandleAndCampaignBody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Handle",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "ad_campaigns",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_CompetitorId_Handle",
                table: "products",
                columns: new[] { "CompetitorId", "Handle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_CompetitorId_Handle",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Handle",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Body",
                table: "ad_campaigns");
        }
    }
}
