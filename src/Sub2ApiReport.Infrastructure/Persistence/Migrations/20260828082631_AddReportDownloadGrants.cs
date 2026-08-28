using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportDownloadGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportDownloadLinkHours",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "ReportDownloadMaxDownloads",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportExternalBaseUrl",
                table: "SystemSettings",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportDownloadGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    TokenCiphertext = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    LifetimeHours = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDownloads = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    DownloadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastDownloadedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDownloadGrants", x => x.Id);
                    table.CheckConstraint("CK_ReportDownloadGrants_DownloadCount", "DownloadCount >= 0 AND (MaxDownloads IS NULL OR (MaxDownloads > 0 AND DownloadCount <= MaxDownloads))");
                    table.CheckConstraint("CK_ReportDownloadGrants_LifetimeHours", "LifetimeHours BETWEEN 1 AND 720");
                    table.ForeignKey(
                        name: "FK_ReportDownloadGrants_DeliveryRecords_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "DeliveryRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportDownloadGrants_ReportSnapshots_ReportSnapshotId",
                        column: x => x.ReportSnapshotId,
                        principalTable: "ReportSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ReportDownloadLinkHours", "ReportDownloadMaxDownloads", "ReportExternalBaseUrl" },
                values: new object[] { 24, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDownloadGrants_DeliveryId",
                table: "ReportDownloadGrants",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportDownloadGrants_ReportSnapshotId_CreatedAtUnixMilliseconds",
                table: "ReportDownloadGrants",
                columns: new[] { "ReportSnapshotId", "CreatedAtUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDownloadGrants_TokenHash",
                table: "ReportDownloadGrants",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportDownloadGrants");

            migrationBuilder.DropColumn(
                name: "ReportDownloadLinkHours",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReportDownloadMaxDownloads",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReportExternalBaseUrl",
                table: "SystemSettings");
        }
    }
}
