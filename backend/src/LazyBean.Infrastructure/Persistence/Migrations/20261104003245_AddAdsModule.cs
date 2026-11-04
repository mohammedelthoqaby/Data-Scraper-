using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "VariantId",
                table: "change_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "change_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "AdId",
                table: "change_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ad_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalAdId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SnapshotUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeliveryStartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryStopDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreativeHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "advertiser_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LibraryUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OwnershipEvidenceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_advertiser_identities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_change_events_AdId",
                table: "change_events",
                column: "AdId");

            migrationBuilder.CreateIndex(
                name: "IX_ad_campaigns_CompetitorId",
                table: "ad_campaigns",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_ad_campaigns_IdentityId",
                table: "ad_campaigns",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_ad_campaigns_IdentityId_CreativeHash",
                table: "ad_campaigns",
                columns: new[] { "IdentityId", "CreativeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_CampaignId",
                table: "ads",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_CompetitorId",
                table: "ads",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_IdentityId",
                table: "ads",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_Platform_ExternalAdId",
                table: "ads",
                columns: new[] { "Platform", "ExternalAdId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ads_Status",
                table: "ads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_advertiser_identities_CompetitorId",
                table: "advertiser_identities",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_advertiser_identities_Platform_ExternalId",
                table: "advertiser_identities",
                columns: new[] { "Platform", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_advertiser_identities_Status",
                table: "advertiser_identities",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_campaigns");

            migrationBuilder.DropTable(
                name: "ads");

            migrationBuilder.DropTable(
                name: "advertiser_identities");

            migrationBuilder.DropIndex(
                name: "IX_change_events_AdId",
                table: "change_events");

            migrationBuilder.DropColumn(
                name: "AdId",
                table: "change_events");

            migrationBuilder.AlterColumn<Guid>(
                name: "VariantId",
                table: "change_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "change_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
