using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Validates PostgreSQL table shape against expected LiteBus schema versions.
/// </summary>
internal static class PostgreSqlSchemaInspector
{
    /// <summary>
    ///     The assembly that embeds shared PostgreSQL schema inspection SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlSchemaInspector).Assembly;

    /// <summary>
    ///     Returns <see langword="true" /> when the table exists in the supplied schema.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="schemaName">The unquoted schema name.</param>
    /// <param name="tableName">The unquoted table name.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns><see langword="true" /> when the table exists; otherwise, <see langword="false" />.</returns>
    public static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var sql = PostgreSqlSqlScriptLoader.Load(Assembly, PostgreSqlSchemaEmbeddedSql.InspectorTableExists);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    /// <summary>
    ///     Returns the set of column names defined on the table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="schemaName">The unquoted schema name.</param>
    /// <param name="tableName">The unquoted table name.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The column names present on the table.</returns>
    public static async Task<HashSet<string>> GetColumnNamesAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var sql = PostgreSqlSqlScriptLoader.Load(Assembly, PostgreSqlSchemaEmbeddedSql.InspectorListColumns);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        var columns = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    ///     Returns the highest schema version inferred from the columns present on the table.
    /// </summary>
    /// <param name="columns">The column names present on the table.</param>
    /// <param name="versionColumns">The ordered column groups introduced by each schema version.</param>
    /// <returns>The highest schema version whose required columns are all present.</returns>
    public static int InferVersionFromColumns(IReadOnlyCollection<string> columns, IReadOnlyList<IReadOnlyList<string>> versionColumns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(versionColumns);

        var inferredVersion = 0;

        for (var index = 0; index < versionColumns.Count; index++)
        {
            var requiredColumns = versionColumns[index];

            foreach (var requiredColumn in requiredColumns)
            {
                if (!columns.Contains(requiredColumn))
                {
                    return inferredVersion;
                }
            }

            inferredVersion = index + 1;
        }

        return inferredVersion;
    }

    /// <summary>
    ///     Validates that all required columns for the supplied schema version exist.
    /// </summary>
    /// <param name="columns">The column names present on the table.</param>
    /// <param name="requiredColumns">The columns required for the target schema version.</param>
    /// <param name="missingColumns">The required columns that are absent from the table.</param>
    public static void ValidateRequiredColumns(
        IReadOnlyCollection<string> columns,
        IReadOnlyList<string> requiredColumns,
        out IReadOnlyList<string> missingColumns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(requiredColumns);

        var missing = new List<string>();

        foreach (var requiredColumn in requiredColumns)
        {
            if (!columns.Contains(requiredColumn))
            {
                missing.Add(requiredColumn);
            }
        }

        missingColumns = missing;
    }

    /// <summary>
    ///     Returns the cumulative column set required through the requested schema version.
    /// </summary>
    /// <param name="versionColumnSets">The ordered column groups introduced by each schema version.</param>
    /// <param name="version">The target schema version.</param>
    /// <returns>The required column names for versions 1 through <paramref name="version" />.</returns>
    internal static IReadOnlyList<string> GetRequiredColumns(
        IReadOnlyList<IReadOnlyList<string>> versionColumnSets,
        int version)
    {
        ArgumentNullException.ThrowIfNull(versionColumnSets);

        if (version <= 0 || version > versionColumnSets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var columns = new List<string>();

        for (var index = 0; index < version; index++)
        {
            columns.AddRange(versionColumnSets[index]);
        }

        return columns;
    }
}
