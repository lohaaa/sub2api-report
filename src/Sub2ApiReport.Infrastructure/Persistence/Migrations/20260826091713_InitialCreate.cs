using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private static readonly string[] columns = new[] { "Id", "InitializedAt", "ReleaseChannel", "Timezone" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SystemSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                InitializedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Timezone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ReleaseChannel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemSettings", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "SystemSettings",
            columns: columns,
            values: new object[] { 1, null, "stable", "Asia/Shanghai" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SystemSettings");
    }
}
