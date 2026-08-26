using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIdentityAndSetup : Migration
{
    private static readonly string[] RecoveryChallengeIndexColumns =
        ["AdministratorId", "ExpiresAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdminUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SingletonKey = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminUsers", x => x.Id);
                table.CheckConstraint("CK_AdminUsers_SingletonKey", "SingletonKey = 1");
            });

        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Actor = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Target = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Result = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                MetadataJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEvents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SetupChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CodeHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                LockedUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SetupChallenges", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AdminUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AdminUserClaims_AdminUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AdminUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AdminUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_AdminUserLogins_AdminUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AdminUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AdminUserTokens",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_AdminUserTokens_AdminUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AdminUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RecoveryChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AdministratorId = table.Column<Guid>(type: "TEXT", nullable: false),
                CodeHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                LockedUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecoveryChallenges", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecoveryChallenges_AdminUsers_AdministratorId",
                    column: x => x.AdministratorId,
                    principalTable: "AdminUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdminUserClaims_UserId",
            table: "AdminUserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AdminUserLogins_UserId",
            table: "AdminUserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "AdminUsers",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "IX_AdminUsers_SingletonKey",
            table: "AdminUsers",
            column: "SingletonKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "AdminUsers",
            column: "NormalizedUserName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_OccurredAt",
            table: "AuditEvents",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_RecoveryChallenges_AdministratorId_ExpiresAt",
            table: "RecoveryChallenges",
            columns: RecoveryChallengeIndexColumns);

        migrationBuilder.CreateIndex(
            name: "IX_SetupChallenges_ExpiresAt",
            table: "SetupChallenges",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AdminUserClaims");

        migrationBuilder.DropTable(
            name: "AdminUserLogins");

        migrationBuilder.DropTable(
            name: "AdminUserTokens");

        migrationBuilder.DropTable(
            name: "AuditEvents");

        migrationBuilder.DropTable(
            name: "RecoveryChallenges");

        migrationBuilder.DropTable(
            name: "SetupChallenges");

        migrationBuilder.DropTable(
            name: "AdminUsers");
    }
}
