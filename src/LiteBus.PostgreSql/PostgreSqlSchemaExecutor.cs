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
    private static readonly Assembly Assembly = typeof(PostgreSqlSchemaExecutor).Assembly;

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

    internal static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        return await PostgreSqlSchemaInspector.TableExistsAsync(connection, schemaName, tableName, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string LoadSharedAddTraceContextColumnScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.SharedAddTraceContextColumn,
            PostgreSqlSchemaSqlTokens.ForStoreTable(options));
    }
}
