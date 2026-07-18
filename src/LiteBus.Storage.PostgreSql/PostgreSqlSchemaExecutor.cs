using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Executes schema SQL scripts and writes optional log entries.
/// </summary>
internal static class PostgreSqlSchemaExecutor
{
    /// <summary>
    ///     Executes one schema SQL batch against an open PostgreSQL connection.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="sql">The SQL batch to execute.</param>
    /// <param name="logger">The schema logger that receives debug output.</param>
    /// <param name="cancellationToken">A token used to cancel command execution.</param>
    /// <returns>A task that completes when the batch finishes executing.</returns>
    internal static async Task ExecuteScriptAsync(
        NpgsqlConnection connection,
        string sql,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(logger);

        logger.Log(PostgreSqlSchemaLogLevel.Debug, "Executing PostgreSQL schema script batch.");

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the requested store table exists.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="schemaName">The unquoted schema name.</param>
    /// <param name="tableName">The unquoted table name.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns><see langword="true" /> when the table exists; otherwise, <see langword="false" />.</returns>
    internal static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        return await PostgreSqlSchemaInspector.TableExistsAsync(connection, schemaName, tableName, cancellationToken)
            .ConfigureAwait(false);
    }
}