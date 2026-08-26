using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddReportSnapshots : Migration
{
    private static readonly string[] CutoffDateStatusColumns = ["CutoffDate", "Status"];
    private static readonly string[] GeneratedAtIdColumns = ["GeneratedAtUnixMilliseconds", "Id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReportConcurrency",
            table: "SystemSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 4);

        migrationBuilder.CreateTable(
            name: "ReportSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CutoffDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Timezone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ConnectionRevision = table.Column<long>(type: "INTEGER", nullable: false),
                GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                GeneratedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                PersonCount = table.Column<int>(type: "INTEGER", nullable: false),
                KeyCount = table.Column<int>(type: "INTEGER", nullable: false),
                FailedSegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                UnassignedSegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                SevenDayActualCost = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                ThirtyDayActualCost = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                CanonicalJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportSnapshots", x => x.Id);
                table.CheckConstraint("CK_ReportSnapshots_ConnectionRevision", "ConnectionRevision > 0");
                table.CheckConstraint("CK_ReportSnapshots_Costs", "SevenDayActualCost >= 0 AND ThirtyDayActualCost >= 0");
                table.CheckConstraint("CK_ReportSnapshots_Counts", "PersonCount >= 0 AND KeyCount >= 0 AND FailedSegmentCount >= 0 AND UnassignedSegmentCount >= 0");
                table.CheckConstraint("CK_ReportSnapshots_SchemaVersion", "SchemaVersion > 0");
            });

        migrationBuilder.UpdateData(
            table: "SystemSettings",
            keyColumn: "Id",
            keyValue: 1,
            column: "ReportConcurrency",
            value: 4);

        migrationBuilder.CreateIndex(
            name: "IX_ReportSnapshots_CutoffDate_Status",
            table: "ReportSnapshots",
            columns: CutoffDateStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_ReportSnapshots_GeneratedAtUnixMilliseconds_Id",
            table: "ReportSnapshots",
            columns: GeneratedAtIdColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReportSnapshots");

        migrationBuilder.DropColumn(
            name: "ReportConcurrency",
            table: "SystemSettings");
    }
}
