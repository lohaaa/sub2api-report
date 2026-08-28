using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteUnixTimeStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sub2ApiUsers_RetiredAt_Status_IsSelected",
                table: "Sub2ApiUsers");

            migrationBuilder.DropIndex(
                name: "IX_SetupChallenges_ExpiresAt",
                table: "SetupChallenges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportRuns_Completion",
                table: "ReportRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReportGenerationRuns_StartedAt_Id",
                table: "ReportGenerationRuns");

            migrationBuilder.DropIndex(
                name: "IX_RecoveryChallenges_AdministratorId_ExpiresAt",
                table: "RecoveryChallenges");

            migrationBuilder.DropIndex(
                name: "IX_ExternalApiKeys_RetiredAt_Status_ExternalId",
                table: "ExternalApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "InitializedAt",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Sub2ApiUsers");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "Sub2ApiUsers");

            migrationBuilder.DropColumn(
                name: "LastSynchronizedAt",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "LastUsersSynchronizedAt",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "SetupChallenges");

            migrationBuilder.DropColumn(
                name: "GeneratedAt",
                table: "ReportSnapshots");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "CollectingAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "DeliveringAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "RenderingAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ReportGenerationRuns");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ReportGenerationRuns");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "RecoveryChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "DeliveryRecords");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "DeliveryParts");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "AdminUsers");

            migrationBuilder.AlterColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "Sub2ApiUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "ExternalApiKeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "OccurredAtUnixMilliseconds",
                table: "AuditEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "AdminUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "UnixTimeMigrationState",
                keyColumn: "Id",
                keyValue: 1,
                column: "Completed",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sub2ApiUsers_RetiredAtUnixMilliseconds_Status_IsSelected",
                table: "Sub2ApiUsers",
                columns: new[] { "RetiredAtUnixMilliseconds", "Status", "IsSelected" });

            migrationBuilder.CreateIndex(
                name: "IX_SetupChallenges_ExpiresAtUnixMilliseconds",
                table: "SetupChallenges",
                column: "ExpiresAtUnixMilliseconds");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportRuns_Completion",
                table: "ReportRuns",
                sql: "(Status IN ('Succeeded', 'PartialFailed', 'Failed') AND CompletedAtUnixMilliseconds IS NOT NULL) OR (Status NOT IN ('Succeeded', 'PartialFailed', 'Failed') AND CompletedAtUnixMilliseconds IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationRuns_StartedAtUnixMilliseconds_Id",
                table: "ReportGenerationRuns",
                columns: new[] { "StartedAtUnixMilliseconds", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryChallenges_AdministratorId_ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges",
                columns: new[] { "AdministratorId", "ExpiresAtUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_RetiredAtUnixMilliseconds_Status_ExternalId",
                table: "ExternalApiKeys",
                columns: new[] { "RetiredAtUnixMilliseconds", "Status", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUnixMilliseconds",
                table: "AuditEvents",
                column: "OccurredAtUnixMilliseconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sub2ApiUsers_RetiredAtUnixMilliseconds_Status_IsSelected",
                table: "Sub2ApiUsers");

            migrationBuilder.DropIndex(
                name: "IX_SetupChallenges_ExpiresAtUnixMilliseconds",
                table: "SetupChallenges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReportRuns_Completion",
                table: "ReportRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReportGenerationRuns_StartedAtUnixMilliseconds_Id",
                table: "ReportGenerationRuns");

            migrationBuilder.DropIndex(
                name: "IX_RecoveryChallenges_AdministratorId_ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges");

            migrationBuilder.DropIndex(
                name: "IX_ExternalApiKeys_RetiredAtUnixMilliseconds_Status_ExternalId",
                table: "ExternalApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_OccurredAtUnixMilliseconds",
                table: "AuditEvents");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InitializedAt",
                table: "SystemSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "SystemSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "Sub2ApiUsers",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "Sub2ApiUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "Sub2ApiUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSynchronizedAt",
                table: "Sub2ApiConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "Sub2ApiConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsersSynchronizedAt",
                table: "Sub2ApiConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Sub2ApiConnections",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "SetupChallenges",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAt",
                table: "SetupChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SetupChallenges",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "SetupChallenges",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "SetupChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "SetupChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GeneratedAt",
                table: "ReportSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ReportSchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CollectingAt",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveringAt",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenderingAt",
                table: "ReportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "ReportRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "ReportGenerationRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "ReportGenerationRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "RecoveryChallenges",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAt",
                table: "RecoveryChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "RecoveryChallenges",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "RecoveryChallenges",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "RecoveryChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "RecoveryChallenges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "NotificationChannels",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "NotificationChannels",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "NotificationChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "NotificationChannels",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<long>(
                name: "LastSeenAtUnixMilliseconds",
                table: "ExternalApiKeys",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "ExternalApiKeys",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsedAt",
                table: "ExternalApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "ExternalApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "DeliveryRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "DeliveryParts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "OccurredAtUnixMilliseconds",
                table: "AuditEvents",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "AuditEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUnixMilliseconds",
                table: "AdminUsers",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AdminUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AdminUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReportSchedules",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "InitializedAt", "UpdatedAt" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "UnixTimeMigrationState",
                keyColumn: "Id",
                keyValue: 1,
                column: "Completed",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Sub2ApiUsers_RetiredAt_Status_IsSelected",
                table: "Sub2ApiUsers",
                columns: new[] { "RetiredAt", "Status", "IsSelected" });

            migrationBuilder.CreateIndex(
                name: "IX_SetupChallenges_ExpiresAt",
                table: "SetupChallenges",
                column: "ExpiresAt");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReportRuns_Completion",
                table: "ReportRuns",
                sql: "(Status IN ('Succeeded', 'PartialFailed', 'Failed') AND CompletedAt IS NOT NULL) OR (Status NOT IN ('Succeeded', 'PartialFailed', 'Failed') AND CompletedAt IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationRuns_StartedAt_Id",
                table: "ReportGenerationRuns",
                columns: new[] { "StartedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryChallenges_AdministratorId_ExpiresAt",
                table: "RecoveryChallenges",
                columns: new[] { "AdministratorId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_RetiredAt_Status_ExternalId",
                table: "ExternalApiKeys",
                columns: new[] { "RetiredAt", "Status", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");
        }
    }
}
