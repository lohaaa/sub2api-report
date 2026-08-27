using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddNotificationDelivery : Migration
{
    private static readonly string[] DeliveryIdPartIndexColumns = ["DeliveryId", "PartIndex"];
    private static readonly string[] RunIdChannelIdColumns = ["RunId", "ChannelId"];
    private static readonly string[] ReportSnapshotIdStartedAtColumns = ["ReportSnapshotId", "StartedAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NotificationChannels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                SmtpHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                SmtpPort = table.Column<int>(type: "INTEGER", nullable: true),
                SmtpSecurity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                SmtpUsername = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                FromAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                FromName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                ToAddressesJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CcAddressesJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                SmtpPasswordCiphertext = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                SmtpPasswordSuffix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                WebhookCiphertext = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                WebhookSuffix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                SignSecretCiphertext = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                SignSecretSuffix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastTestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastTestSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
                LastTestCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                table.CheckConstraint("CK_NotificationChannels_EmailExclusive", "Type = 'Email' OR (SmtpHost IS NULL AND SmtpPort IS NULL AND SmtpSecurity IS NULL AND SmtpUsername IS NULL AND FromAddress IS NULL AND FromName IS NULL AND ToAddressesJson IS NULL AND CcAddressesJson IS NULL AND SmtpPasswordCiphertext IS NULL AND SmtpPasswordSuffix IS NULL)");
                table.CheckConstraint("CK_NotificationChannels_EmailFields", "Type <> 'Email' OR (SmtpHost IS NOT NULL AND SmtpPort IS NOT NULL AND SmtpSecurity IS NOT NULL AND FromAddress IS NOT NULL AND ToAddressesJson IS NOT NULL)");
                table.CheckConstraint("CK_NotificationChannels_EmailSecret", "Type <> 'Email' OR (SmtpPasswordCiphertext IS NULL AND SmtpPasswordSuffix IS NULL) OR (SmtpPasswordCiphertext IS NOT NULL AND SmtpPasswordSuffix IS NOT NULL)");
                table.CheckConstraint("CK_NotificationChannels_Name", "length(Name) >= 1");
                table.CheckConstraint("CK_NotificationChannels_Revision", "Revision > 0");
                table.CheckConstraint("CK_NotificationChannels_WebhookExclusive", "Type IN ('DingTalk', 'Feishu') OR (WebhookCiphertext IS NULL AND WebhookSuffix IS NULL AND SignSecretCiphertext IS NULL AND SignSecretSuffix IS NULL)");
                table.CheckConstraint("CK_NotificationChannels_WebhookFields", "Type NOT IN ('DingTalk', 'Feishu') OR (WebhookCiphertext IS NOT NULL AND WebhookSuffix IS NOT NULL AND SignSecretCiphertext IS NOT NULL AND SignSecretSuffix IS NOT NULL)");
            });

        migrationBuilder.CreateTable(
            name: "ReportRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ReportSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportRuns", x => x.Id);
                table.CheckConstraint("CK_ReportRuns_Completion", "(Status = 'Running' AND CompletedAt IS NULL) OR (Status <> 'Running' AND CompletedAt IS NOT NULL)");
                table.CheckConstraint("CK_ReportRuns_Status", "Status IN ('Running', 'Succeeded', 'PartialFailed', 'Failed')");
                table.CheckConstraint("CK_ReportRuns_Trigger", "Trigger = 'ManualDelivery'");
                table.ForeignKey(
                    name: "FK_ReportRuns_ReportSnapshots_ReportSnapshotId",
                    column: x => x.ReportSnapshotId,
                    principalTable: "ReportSnapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DeliveryRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChannelType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ChannelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliveryRecords", x => x.Id);
                table.CheckConstraint("CK_DeliveryRecords_Attempts", "Attempts >= 0");
                table.CheckConstraint("CK_DeliveryRecords_ChannelType", "ChannelType IN ('Email', 'DingTalk', 'Feishu')");
                table.CheckConstraint("CK_DeliveryRecords_Status", "Status IN ('Pending', 'Sending', 'Succeeded', 'Failed')");
                table.ForeignKey(
                    name: "FK_DeliveryRecords_NotificationChannels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "NotificationChannels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryRecords_ReportRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "ReportRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DeliveryParts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                PartIndex = table.Column<int>(type: "INTEGER", nullable: false),
                PartCount = table.Column<int>(type: "INTEGER", nullable: false),
                PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliveryParts", x => x.Id);
                table.CheckConstraint("CK_DeliveryParts_Attempts", "Attempts >= 0");
                table.CheckConstraint("CK_DeliveryParts_Index", "PartIndex >= 0 AND PartCount >= 1 AND PartIndex < PartCount");
                table.CheckConstraint("CK_DeliveryParts_Status", "Status IN ('Pending', 'Succeeded', 'Failed')");
                table.ForeignKey(
                    name: "FK_DeliveryParts_DeliveryRecords_DeliveryId",
                    column: x => x.DeliveryId,
                    principalTable: "DeliveryRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryParts_DeliveryId_PartIndex",
            table: "DeliveryParts",
            columns: DeliveryIdPartIndexColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryRecords_ChannelId",
            table: "DeliveryRecords",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryRecords_RunId_ChannelId",
            table: "DeliveryRecords",
            columns: RunIdChannelIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReportRuns_IdempotencyKey",
            table: "ReportRuns",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReportRuns_ReportSnapshotId_StartedAt",
            table: "ReportRuns",
            columns: ReportSnapshotIdStartedAtColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeliveryParts");

        migrationBuilder.DropTable(
            name: "DeliveryRecords");

        migrationBuilder.DropTable(
            name: "NotificationChannels");

        migrationBuilder.DropTable(
            name: "ReportRuns");
    }
}
