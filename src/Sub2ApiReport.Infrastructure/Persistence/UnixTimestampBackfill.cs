using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Sub2ApiReport.Infrastructure.Persistence;

public static class UnixTimestampBackfill
{
    public static async Task ApplyAsync(
        ReportDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var mappings = GetMappings(dbContext.Model);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != global::System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var columnsByTable = new Dictionary<(string? Schema, string Table), HashSet<string>>();
            foreach (var mapping in mappings)
            {
                var tableKey = (mapping.Schema, mapping.TableName);
                if (!columnsByTable.TryGetValue(tableKey, out var columns))
                {
                    columns = await GetColumnsAsync(
                        connection,
                        transaction,
                        mapping.Schema,
                        mapping.TableName,
                        cancellationToken);
                    columnsByTable.Add(tableKey, columns);
                }

                if (!columns.Contains(mapping.LegacyColumn)
                    || !columns.Contains(mapping.UnixColumn)
                    || mapping.KeyColumns.Any(column => !columns.Contains(column)))
                {
                    continue;
                }

                await BackfillAsync(connection, transaction, mapping, cancellationToken);
            }

            await MarkCompletedAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static TimestampMapping[] GetMappings(IModel model)
    {
        var mappings = new List<TimestampMapping>();
        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            var primaryKey = entityType.FindPrimaryKey();
            if (tableName is null || primaryKey is null)
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var keyColumns = primaryKey.Properties
                .Select(property => property.GetColumnName(storeObject)
                    ?? throw new InvalidOperationException(
                        $"Primary key column mapping is missing for {entityType.Name}."))
                .ToArray();
            foreach (var property in entityType.GetProperties().Where(property =>
                property.ClrType == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset?)))
            {
                var unixColumn = property.GetColumnName(storeObject)
                    ?? throw new InvalidOperationException(
                        $"Timestamp column mapping is missing for {entityType.Name}.{property.Name}.");
                var legacyColumn = property.Name;
                if (string.Equals(unixColumn, legacyColumn, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Timestamp property {entityType.Name}.{property.Name} is not mapped to Unix milliseconds.");
                }

                mappings.Add(new TimestampMapping(
                    tableName,
                    entityType.GetSchema(),
                    keyColumns,
                    legacyColumn,
                    unixColumn));
            }
        }

        return mappings
            .Distinct()
            .OrderBy(mapping => mapping.TableName, StringComparer.Ordinal)
            .ThenBy(mapping => mapping.LegacyColumn, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string? schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        if (schema is null)
        {
            command.CommandText = "SELECT name FROM pragma_table_info(@tableName)";
        }
        else
        {
            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = "@schema";
            schemaParameter.Value = schema;
            command.Parameters.Add(schemaParameter);
            command.CommandText = "SELECT name FROM pragma_table_info(@tableName, @schema)";
        }

        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }


    private static async Task BackfillAsync(
        DbConnection connection,
        DbTransaction transaction,
        TimestampMapping mapping,
        CancellationToken cancellationToken)
    {
        var rows = new List<TimestampRow>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {string.Join(", ", mapping.KeyColumns.Select(Quote))}, "
                + $"{Quote(mapping.LegacyColumn)} FROM {Qualify(mapping.Schema, mapping.TableName)} "
                + $"WHERE {Quote(mapping.LegacyColumn)} IS NOT NULL";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var keys = new object[mapping.KeyColumns.Count];
                for (var index = 0; index < keys.Length; index++)
                {
                    keys[index] = reader.GetValue(index);
                }

                var serialized = reader.GetString(mapping.KeyColumns.Count);
                if (!DateTimeOffset.TryParse(
                    serialized,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out var timestamp))
                {
                    throw new InvalidOperationException(
                        $"Stored timestamp in {mapping.TableName}.{mapping.LegacyColumn} is invalid.");
                }

                rows.Add(new TimestampRow(keys, timestamp.ToUnixTimeMilliseconds()));
            }
        }

        foreach (var row in rows)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            var predicates = new string[mapping.KeyColumns.Count];
            for (var index = 0; index < mapping.KeyColumns.Count; index++)
            {
                var parameter = update.CreateParameter();
                parameter.ParameterName = $"@key{index}";
                parameter.Value = row.Keys[index];
                update.Parameters.Add(parameter);
                predicates[index] = $"{Quote(mapping.KeyColumns[index])} = {parameter.ParameterName}";
            }

            var timestampParameter = update.CreateParameter();
            timestampParameter.ParameterName = "@unixMilliseconds";
            timestampParameter.Value = row.UnixMilliseconds;
            update.Parameters.Add(timestampParameter);
            update.CommandText = $"UPDATE {Qualify(mapping.Schema, mapping.TableName)} "
                + $"SET {Quote(mapping.UnixColumn)} = {timestampParameter.ParameterName} "
                + $"WHERE {string.Join(" AND ", predicates)}";
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Timestamp backfill for {mapping.TableName}.{mapping.LegacyColumn} did not update exactly one row.");
            }
        }
    }

    private static async Task MarkCompletedAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE \"UnixTimeMigrationState\" SET \"Completed\" = 1 WHERE \"Id\" = 1";
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Unix timestamp migration guard did not update exactly one row.");
        }
    }

    private static string Qualify(string? schema, string tableName) => schema is null
        ? Quote(tableName)
        : $"{Quote(schema)}.{Quote(tableName)}";

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record TimestampMapping(
        string TableName,
        string? Schema,
        IReadOnlyList<string> KeyColumns,
        string LegacyColumn,
        string UnixColumn);

    private sealed record TimestampRow(object[] Keys, long UnixMilliseconds);
}
