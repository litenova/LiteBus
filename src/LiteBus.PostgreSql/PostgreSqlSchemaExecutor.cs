using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.PostgreSql;

/// <summary>
///     Executes schema SQL scripts and writes optional log entries.
/// </summary>
internal static class PostgreSqlSchemaExecutor
{
    /// <summary>
    ///     The assembly that embeds shared PostgreSQL schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlSchemaExecutor).Assembly;

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

        await using var command = connection.CreateCommand();
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

    /// <summary>
    ///     Loads and renders the shared version 2 upgrade script that adds <c>trace_context</c>.
    /// </summary>
    /// <param name="options">The store table options used to replace SQL placeholders.</param>
    /// <returns>The rendered upgrade SQL batch.</returns>
    internal static string LoadSharedAddTraceContextColumnScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.SharedAddTraceContextColumn,
            PostgreSqlSchemaSqlTokens.ForStoreTable(options));
    }
}
