using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Sub2ApiReport.Infrastructure.Persistence;

public static class LegacySchedulingMigrationAlias
{
    private static readonly (string Table, string Column)[] RequiredSchema =
    [
        ("ReportSchedules", "DayOfMonth"),
        ("QRTZ_JOB_DETAILS", "JOB_NAME"),
        ("ReportRuns", "Attempt"),
        ("ReportRuns", "ScheduleId"),
        ("ReportRuns", "StartedAtUnixMilliseconds"),
        ("ReportGenerationRuns", "ReportRunId"),
    ];

    public static async Task ApplyAsync(
        ReportDbContext dbContext,
        string legacyMigrationId,
        string replacementMigrationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyMigrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementMigrationId);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != global::System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken))
        {
            return;
        }

        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        if (!applied.Contains(legacyMigrationId) || applied.Contains(replacementMigrationId))
        {
            return;
        }

        foreach (var (table, column) in RequiredSchema)
        {
            if (!await ColumnExistsAsync(connection, table, column, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Legacy scheduling migration is recorded, but required schema {table}.{column} is missing.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE \"__EFMigrationsHistory\" "
            + "SET \"MigrationId\" = @replacement "
            + "WHERE \"MigrationId\" = @legacy";
        AddParameter(command, "@replacement", replacementMigrationId);
        AddParameter(command, "@legacy", legacyMigrationId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException(
                "Legacy scheduling migration alias did not update exactly one history row.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master "
            + "WHERE type = 'table' AND name = @table";
        AddParameter(command, "@table", tableName);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            global::System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info({QuoteLiteral(tableName)}) "
            + "WHERE name = @column";
        AddParameter(command, "@column", columnName);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            global::System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string QuoteLiteral(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
