using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.PostgreSql;

/// <summary>
///     Reads and writes LiteBus schema version metadata in PostgreSQL.
/// </summary>
internal static class PostgreSqlSchemaVersionStore
{
    private static readonly Assembly Assembly = typeof(PostgreSqlSchemaVersionStore).Assembly;

    /// <summary>
    ///     Creates the schema version metadata table when it does not exist.
    /// </summary>
    public static async Task EnsureMetadataTableAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Ensuring metadata table '{options.MetadataSchemaName}.{options.MetadataTableName}' exists.");

        var sql = GetMetadataCreateScript(options);
        await PostgreSqlSchemaExecutor.ExecuteScriptAsync(connection, sql, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the recorded schema version, or <c>0</c> when no row exists or the metadata table has not been created.
    /// </summary>
    public static async Task<int> GetVersionAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        string component,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!await PostgreSqlSchemaInspector.TableExistsAsync(
                connection,
                options.MetadataSchemaName,
                options.MetadataTableName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return 0;
        }

        var sql = GetMetadataSelectVersionScript(options);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", component);
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int version ? version : 0;
    }

    /// <summary>
    ///     Records the applied schema version for one store table.
    /// </summary>
    public static async Task SetVersionAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        string component,
        string schemaName,
        string tableName,
        int version,
        DateTimeOffset appliedAt,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Schema version must be positive.");
        }

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Recording schema version {version} for {component} table '{schemaName}.{tableName}'.");

        var sql = GetMetadataUpsertVersionScript(options);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", component);
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("appliedAt", appliedAt);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the SQL script that creates the schema version metadata table.
    /// </summary>
    public static string GetMetadataCreateScript(IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataCreate,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }

    internal static string GetMetadataSelectVersionScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataSelectVersion,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }

    internal static string GetMetadataUpsertVersionScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataUpsertVersion,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }
}
