using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShortMonthStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportSchedules_DayOfMonth",
                table: "ReportSchedules");

            migrationBuilder.AddColumn<string>(
                name: "ShortMonthStrategy",
                table: "ReportSchedules",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ReportSchedules",
                keyColumn: "Id",
                keyValue: 1,
                column: "ShortMonthStrategy",
                value: "UseLastDay");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportSchedules_DayOfMonth",
                table: "ReportSchedules",
                sql: "DayOfMonth BETWEEN 1 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportSchedules_ShortMonthStrategy",
                table: "ReportSchedules",
                sql: "ShortMonthStrategy IN ('UseLastDay', 'SkipMonth')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportSchedules_DayOfMonth",
                table: "ReportSchedules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportSchedules_ShortMonthStrategy",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "ShortMonthStrategy",
                table: "ReportSchedules");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportSchedules_DayOfMonth",
                table: "ReportSchedules",
                sql: "DayOfMonth BETWEEN 1 AND 28");
        }
    }
}
