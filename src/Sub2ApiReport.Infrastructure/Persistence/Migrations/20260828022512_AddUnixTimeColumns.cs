using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnixTimeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InitializedAtUnixMilliseconds",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "Sub2ApiUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RetiredAtUnixMilliseconds",
                table: "Sub2ApiUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastSynchronizedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastTestedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastUsersSynchronizedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConsumedAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LockedUntilUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevokedAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "ReportSchedules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CollectingAtUnixMilliseconds",
                table: "ReportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompletedAtUnixMilliseconds",
                table: "ReportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeliveringAtUnixMilliseconds",
                table: "ReportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RenderingAtUnixMilliseconds",
                table: "ReportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompletedAtUnixMilliseconds",
                table: "ReportGenerationRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConsumedAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LockedUntilUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevokedAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastTestedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "ExternalApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastUsedAtUnixMilliseconds",
                table: "ExternalApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RetiredAtUnixMilliseconds",
                table: "ExternalApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SentAtUnixMilliseconds",
                table: "DeliveryRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SentAtUnixMilliseconds",
                table: "DeliveryParts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OccurredAtUnixMilliseconds",
                table: "AuditEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "AdminUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LockoutEndUnixMilliseconds",
                table: "AdminUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReportSchedules",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAtUnixMilliseconds",
                value: null);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "InitializedAtUnixMilliseconds", "UpdatedAtUnixMilliseconds" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitializedAtUnixMilliseconds",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUnixMilliseconds",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUnixMilliseconds",
                table: "Sub2ApiUsers");

            migrationBuilder.DropColumn(
                name: "RetiredAtUnixMilliseconds",
                table: "Sub2ApiUsers");

            migrationBuilder.DropColumn(
                name: "LastSynchronizedAtUnixMilliseconds",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "LastTestedAtUnixMilliseconds",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "LastUsersSynchronizedAtUnixMilliseconds",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUnixMilliseconds",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "ConsumedAtUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "LockedUntilUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "RevokedAtUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUnixMilliseconds",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "CollectingAtUnixMilliseconds",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "CompletedAtUnixMilliseconds",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "DeliveringAtUnixMilliseconds",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "RenderingAtUnixMilliseconds",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "CompletedAtUnixMilliseconds",
                table: "ReportGenerationRuns");

            migrationBuilder.DropColumn(
                name: "ConsumedAtUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "LockedUntilUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "RevokedAtUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMilliseconds",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastTestedAtUnixMilliseconds",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUnixMilliseconds",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUnixMilliseconds",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "LastUsedAtUnixMilliseconds",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "RetiredAtUnixMilliseconds",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "SentAtUnixMilliseconds",
                table: "DeliveryRecords");

            migrationBuilder.DropColumn(
                name: "SentAtUnixMilliseconds",
                table: "DeliveryParts");

            migrationBuilder.DropColumn(
                name: "OccurredAtUnixMilliseconds",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMilliseconds",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "LockoutEndUnixMilliseconds",
                table: "AdminUsers");
        }
    }
}
