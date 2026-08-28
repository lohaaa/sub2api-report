using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexibleReportWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WindowSummaryJson",
                table: "ReportSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindowSpecsJson",
                table: "ReportSchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedWindowsJson",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindowSpecsJson",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReportSchedules",
                keyColumn: "Id",
                keyValue: 1,
                column: "WindowSpecsJson",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WindowSummaryJson",
                table: "ReportSnapshots");

            migrationBuilder.DropColumn(
                name: "WindowSpecsJson",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "ResolvedWindowsJson",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "WindowSpecsJson",
                table: "ReportRuns");
        }
    }
}
