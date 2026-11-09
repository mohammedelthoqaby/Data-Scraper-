using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdPreviewsPricesAndIngestBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ads_Platform_ExternalAdId",
                table: "ads");

            migrationBuilder.AddColumn<decimal>(
                name: "AdvertisedPrice",
                table: "ads",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvertisedQuantity",
                table: "ads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreativeFormat",
                table: "ads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkMethod",
                table: "ads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkVersion",
                table: "ads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PreviewUrl",
                table: "ads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ad_ingest_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Received = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Duplicate = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_ingest_batches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ads_Platform_IdentityId_ExternalAdId",
                table: "ads",
                columns: new[] { "Platform", "IdentityId", "ExternalAdId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ad_ingest_batches_CompetitorId",
                table: "ad_ingest_batches",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_ad_ingest_batches_IdentityId_ReceivedAt",
                table: "ad_ingest_batches",
                columns: new[] { "IdentityId", "ReceivedAt" });

            // Ads saved since the Meta ingest change lost their campaign link (the new-ad path did not set it), and
            // older rows may lack it too. Link each ad to the campaign of its account and creative hash.
            migrationBuilder.Sql(@"
                UPDATE ads AS a
                SET ""CampaignId"" = c.""Id""
                FROM ad_campaigns AS c
                WHERE a.""CampaignId"" IS NULL
                  AND c.""IdentityId"" = a.""IdentityId""
                  AND c.""CreativeHash"" = a.""CreativeHash"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_ingest_batches");

            migrationBuilder.DropIndex(
                name: "IX_ads_Platform_IdentityId_ExternalAdId",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "AdvertisedPrice",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "AdvertisedQuantity",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "CreativeFormat",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "LinkMethod",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "LinkVersion",
                table: "ads");

            migrationBuilder.DropColumn(
                name: "PreviewUrl",
                table: "ads");

            migrationBuilder.CreateIndex(
                name: "IX_ads_Platform_ExternalAdId",
                table: "ads",
                columns: new[] { "Platform", "ExternalAdId" },
                unique: true);
        }
    }
}
