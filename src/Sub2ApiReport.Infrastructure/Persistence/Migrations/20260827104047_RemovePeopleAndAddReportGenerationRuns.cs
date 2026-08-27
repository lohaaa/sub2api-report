using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePeopleAndAddReportGenerationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonApiKeyAssignments");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportSnapshots_Counts",
                table: "ReportSnapshots");

            migrationBuilder.DropColumn(
                name: "FailedSegmentCount",
                table: "ReportSnapshots");

            migrationBuilder.RenameColumn(
                name: "UnassignedSegmentCount",
                table: "ReportSnapshots",
                newName: "UserCount");

            migrationBuilder.RenameColumn(
                name: "PersonCount",
                table: "ReportSnapshots",
                newName: "FailedRangeCount");

            migrationBuilder.CreateTable(
                name: "ReportGenerationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ConnectionRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReportSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportGenerationRuns", x => x.Id);
                    table.CheckConstraint("CK_ReportGenerationRuns_Status", "Status IN ('Running', 'Succeeded', 'Failed')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportSnapshots_Counts",
                table: "ReportSnapshots",
                sql: "UserCount >= 0 AND KeyCount >= 0 AND FailedRangeCount >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationRuns_StartedAt_Id",
                table: "ReportGenerationRuns",
                columns: new[] { "StartedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationRuns_Status",
                table: "ReportGenerationRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportGenerationRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportSnapshots_Counts",
                table: "ReportSnapshots");

            migrationBuilder.RenameColumn(
                name: "UserCount",
                table: "ReportSnapshots",
                newName: "UnassignedSegmentCount");

            migrationBuilder.RenameColumn(
                name: "FailedRangeCount",
                table: "ReportSnapshots",
                newName: "PersonCount");

            migrationBuilder.AddColumn<int>(
                name: "FailedSegmentCount",
                table: "ReportSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.CheckConstraint("CK_People_Revision", "Revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "PersonApiKeyAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalApiKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonApiKeyAssignments", x => x.Id);
                    table.CheckConstraint("CK_PersonApiKeyAssignments_DateRange", "ValidTo IS NULL OR ValidTo >= ValidFrom");
                    table.CheckConstraint("CK_PersonApiKeyAssignments_Revision", "Revision > 0");
                    table.ForeignKey(
                        name: "FK_PersonApiKeyAssignments_ExternalApiKeys_ExternalApiKeyId",
                        column: x => x.ExternalApiKeyId,
                        principalTable: "ExternalApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonApiKeyAssignments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportSnapshots_Counts",
                table: "ReportSnapshots",
                sql: "PersonCount >= 0 AND KeyCount >= 0 AND FailedSegmentCount >= 0 AND UnassignedSegmentCount >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_People_Code",
                table: "People",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_IsActive_DisplayName",
                table: "People",
                columns: new[] { "IsActive", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonApiKeyAssignments_ExternalApiKeyId_ValidFrom_ValidTo",
                table: "PersonApiKeyAssignments",
                columns: new[] { "ExternalApiKeyId", "ValidFrom", "ValidTo" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonApiKeyAssignments_PersonId_ValidFrom_ValidTo",
                table: "PersonApiKeyAssignments",
                columns: new[] { "PersonId", "ValidFrom", "ValidTo" });
        }
    }
}
