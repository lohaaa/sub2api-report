using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSub2ApiAndPeople : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExternalApiKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ExternalId = table.Column<long>(type: "INTEGER", nullable: false),
                NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                GroupId = table.Column<long>(type: "INTEGER", nullable: true),
                LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                RetiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalApiKeys", x => x.Id);
                table.CheckConstraint("CK_ExternalApiKeys_ExternalId", "ExternalId > 0");
                table.CheckConstraint("CK_ExternalApiKeys_GroupId", "GroupId IS NULL OR GroupId > 0");
            });

        migrationBuilder.CreateTable(
            name: "People",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "NOCASE"),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_People", x => x.Id);
                table.CheckConstraint("CK_People_Revision", "Revision > 0");
            });

        migrationBuilder.CreateTable(
            name: "Sub2ApiConnections",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                AdminApiKeyCiphertext = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                AdminApiKeySuffix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                UserId = table.Column<long>(type: "INTEGER", nullable: false),
                CodexGroupId = table.Column<long>(type: "INTEGER", nullable: true),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastTestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastTestSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
                LastTestCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                LastSynchronizedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastSynchronizedKeyCount = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sub2ApiConnections", x => x.Id);
                table.CheckConstraint("CK_Sub2ApiConnections_CodexGroupId", "CodexGroupId IS NULL OR CodexGroupId > 0");
                table.CheckConstraint("CK_Sub2ApiConnections_Revision", "Revision > 0");
                table.CheckConstraint("CK_Sub2ApiConnections_Singleton", "Id = 1");
                table.CheckConstraint("CK_Sub2ApiConnections_UserId", "UserId > 0");
            });

        migrationBuilder.CreateTable(
            name: "PersonApiKeyAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                ExternalApiKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ValidTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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

        migrationBuilder.CreateIndex(
            name: "IX_ExternalApiKeys_ExternalId",
            table: "ExternalApiKeys",
            column: "ExternalId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExternalApiKeys_RetiredAt_Status_ExternalId",
            table: "ExternalApiKeys",
            columns: ["RetiredAt", "Status", "ExternalId"]);

        migrationBuilder.CreateIndex(
            name: "IX_People_Code",
            table: "People",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_People_IsActive_DisplayName",
            table: "People",
            columns: ["IsActive", "DisplayName"]);

        migrationBuilder.CreateIndex(
            name: "IX_PersonApiKeyAssignments_ExternalApiKeyId_ValidFrom_ValidTo",
            table: "PersonApiKeyAssignments",
            columns: ["ExternalApiKeyId", "ValidFrom", "ValidTo"]);

        migrationBuilder.CreateIndex(
            name: "IX_PersonApiKeyAssignments_PersonId_ValidFrom_ValidTo",
            table: "PersonApiKeyAssignments",
            columns: ["PersonId", "ValidFrom", "ValidTo"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PersonApiKeyAssignments");

        migrationBuilder.DropTable(
            name: "Sub2ApiConnections");

        migrationBuilder.DropTable(
            name: "ExternalApiKeys");

        migrationBuilder.DropTable(
            name: "People");
    }
}
