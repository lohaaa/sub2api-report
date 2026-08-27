using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiUserScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sub2ApiConnections_UserId",
                table: "Sub2ApiConnections");

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "LastSynchronizedUserCount",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsersSynchronizedAt",
                table: "Sub2ApiConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserScopeMode",
                table: "Sub2ApiConnections",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "SelectedUsers");

            migrationBuilder.AddColumn<Guid>(
                name: "Sub2ApiUserId",
                table: "ExternalApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sub2ApiUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<long>(type: "INTEGER", nullable: false),
                    EmailSnapshot = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    UsernameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sub2ApiUsers", x => x.Id);
                    table.CheckConstraint("CK_Sub2ApiUsers_ExternalId", "ExternalId > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sub2ApiConnections_UserId",
                table: "Sub2ApiConnections",
                sql: "UserId IS NULL OR UserId > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiKeys_Sub2ApiUserId",
                table: "ExternalApiKeys",
                column: "Sub2ApiUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sub2ApiUsers_ExternalId",
                table: "Sub2ApiUsers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sub2ApiUsers_RetiredAt_Status_IsSelected",
                table: "Sub2ApiUsers",
                columns: new[] { "RetiredAt", "Status", "IsSelected" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalApiKeys_Sub2ApiUsers_Sub2ApiUserId",
                table: "ExternalApiKeys",
                column: "Sub2ApiUserId",
                principalTable: "Sub2ApiUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExternalApiKeys_Sub2ApiUsers_Sub2ApiUserId",
                table: "ExternalApiKeys");

            migrationBuilder.DropTable(
                name: "Sub2ApiUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sub2ApiConnections_UserId",
                table: "Sub2ApiConnections");

            migrationBuilder.DropIndex(
                name: "IX_ExternalApiKeys_Sub2ApiUserId",
                table: "ExternalApiKeys");

            migrationBuilder.DropColumn(
                name: "LastSynchronizedUserCount",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "LastUsersSynchronizedAt",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "UserScopeMode",
                table: "Sub2ApiConnections");

            migrationBuilder.DropColumn(
                name: "Sub2ApiUserId",
                table: "ExternalApiKeys");

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "Sub2ApiConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sub2ApiConnections_UserId",
                table: "Sub2ApiConnections",
                sql: "UserId > 0");
        }
    }
}
