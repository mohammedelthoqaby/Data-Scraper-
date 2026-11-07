using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectorJobProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProgressDone",
                table: "collector_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressLabel",
                table: "collector_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressTotal",
                table: "collector_jobs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProgressDone",
                table: "collector_jobs");

            migrationBuilder.DropColumn(
                name: "ProgressLabel",
                table: "collector_jobs");

            migrationBuilder.DropColumn(
                name: "ProgressTotal",
                table: "collector_jobs");
        }
    }
}
