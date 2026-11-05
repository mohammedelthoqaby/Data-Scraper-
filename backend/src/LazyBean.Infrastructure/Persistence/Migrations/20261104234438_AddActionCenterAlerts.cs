using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActionCenterAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_histories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "alert_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdMoreExpensiveGapPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceDropPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    LazyBeanAboveCheapestPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    OutOfStockCompetitorCount = table.Column<int>(type: "integer", nullable: false),
                    MarginRoomBelowCheapestPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    UncarriedBrandCompetitorCount = table.Column<int>(type: "integer", nullable: false),
                    BigCampaignDiscountCount = table.Column<int>(type: "integer", nullable: false),
                    ProlongedGapDays = table.Column<int>(type: "integer", nullable: false),
                    MaterialChangePercentPoints = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReasonAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SuggestedAction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SuggestedActionAr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceProductTitle = table.Column<string>(type: "text", nullable: true),
                    ReferenceProductUrl = table.Column<string>(type: "text", nullable: true),
                    CompetitorProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompetitorProductTitle = table.Column<string>(type: "text", nullable: true),
                    CompetitorProductUrl = table.Column<string>(type: "text", nullable: true),
                    CompetitorName = table.Column<string>(type: "text", nullable: true),
                    AdLibraryUrl = table.Column<string>(type: "text", nullable: true),
                    IsBestseller = table.Column<bool>(type: "boolean", nullable: false),
                    EgpImpact = table.Column<decimal>(type: "numeric", nullable: false),
                    MetricValuesJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DismissNote = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdentityKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_histories_AlertId",
                table: "alert_histories",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_IdentityKey",
                table: "alerts",
                column: "IdentityKey");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_Severity",
                table: "alerts",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_Status",
                table: "alerts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_histories");

            migrationBuilder.DropTable(
                name: "alert_settings");

            migrationBuilder.DropTable(
                name: "alerts");
        }
    }
}
