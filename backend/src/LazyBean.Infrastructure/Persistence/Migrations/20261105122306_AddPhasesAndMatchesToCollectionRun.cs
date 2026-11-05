using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LazyBean.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhasesAndMatchesToCollectionRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchesCount",
                table: "collection_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhasesJson",
                table: "collection_runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchesCount",
                table: "collection_runs");

            migrationBuilder.DropColumn(
                name: "PhasesJson",
                table: "collection_runs");
        }
    }
}
