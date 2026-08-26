using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sub2ApiReport.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDynamicSystemSettings : Migration
{
    private static readonly string[] Columns =
        ["BackupRetentionCount", "LogLevel", "ReportRetentionMonths", "Revision", "UpdatedAt"];

    private static readonly object[] Values = [10, "Information", 12, 1L, null];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BackupRetentionCount",
            table: "SystemSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "LogLevel",
            table: "SystemSettings",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "ReportRetentionMonths",
            table: "SystemSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<long>(
            name: "Revision",
            table: "SystemSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "SystemSettings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.UpdateData(
            table: "SystemSettings",
            keyColumn: "Id",
            keyValue: 1,
            columns: Columns,
            values: Values);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BackupRetentionCount",
            table: "SystemSettings");

        migrationBuilder.DropColumn(
            name: "LogLevel",
            table: "SystemSettings");

        migrationBuilder.DropColumn(
            name: "ReportRetentionMonths",
            table: "SystemSettings");

        migrationBuilder.DropColumn(
            name: "Revision",
            table: "SystemSettings");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "SystemSettings");
    }
}
