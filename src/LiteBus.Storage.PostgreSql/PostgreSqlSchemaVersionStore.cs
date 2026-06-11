using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Reads and writes LiteBus schema version metadata in PostgreSQL.
/// </summary>
internal static class PostgreSqlSchemaVersionStore
{
    /// <summary>
    ///     The assembly that embeds shared PostgreSQL schema metadata SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlSchemaVersionStore).Assembly;

    /// <summary>
    ///     Creates the schema version metadata table when it does not exist.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the metadata table exists.</returns>
    public static Task EnsureMetadataTableAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return EnsureMetadataTableAsync(
            context.Connection,
            context.Options,
            context.Logger,
            cancellationToken);
    }

    /// <summary>
    ///     Creates the schema version metadata table when it does not exist.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the metadata table exists.</returns>
    public static async Task EnsureMetadataTableAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var metadataTable = PostgreSqlTableReference.ForMetadata(options);

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Ensuring metadata table '{metadataTable.QualifiedName}' exists.");

        var sql = GetMetadataCreateScript(options);
        await PostgreSqlSchemaExecutor.ExecuteScriptAsync(connection, sql, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the recorded schema version, or <c>0</c> when no row exists or the metadata table has not been created.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="component">The LiteBus store component name.</param>
    /// <param name="schemaName">The unquoted schema name of the store table.</param>
    /// <param name="tableName">The unquoted table name of the store table.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The recorded schema version, or <c>0</c> when no version row exists.</returns>
    public static Task<int> GetVersionAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        string component,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        var context = PostgreSqlSchemaOperationContext.ForVersionLookup(
            connection,
            options,
            component,
            PostgreSqlTableReference.Create(schemaName, tableName),
            NullPostgreSqlSchemaLogger.Instance);

        return GetVersionAsync(context, cancellationToken);
    }

    /// <summary>
    ///     Returns the recorded schema version, or <c>0</c> when no row exists or the metadata table has not been created.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The recorded schema version, or <c>0</c> when no version row exists.</returns>
    public static async Task<int> GetVersionAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await PostgreSqlSchemaInspector.TableExistsAsync(
                    context.Connection,
                    context.MetadataTable,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return 0;
        }

        var sql = GetMetadataSelectVersionScript(context.Options);

        await using var command = context.Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", context.Component);
        command.Parameters.AddWithValue("schemaName", context.StoreTable.SchemaName);
        command.Parameters.AddWithValue("tableName", context.StoreTable.TableName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int version ? version : 0;
    }

    /// <summary>
    ///     Records the applied schema version for one store table.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="version">The schema version to record.</param>
    /// <param name="appliedAt">The timestamp stored with the version row.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the version row is written.</returns>
    public static async Task SetVersionAsync(
        PostgreSqlSchemaOperationContext context,
        int version,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Schema version must be positive.");
        }

        context.Logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Recording schema version {version} for {context.Component} table '{context.StoreTable.QualifiedName}'.");

        var sql = GetMetadataUpsertVersionScript(context.Options);

        await using var command = context.Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", context.Component);
        command.Parameters.AddWithValue("schemaName", context.StoreTable.SchemaName);
        command.Parameters.AddWithValue("tableName", context.StoreTable.TableName);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("appliedAt", appliedAt);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Records the applied schema version for one store table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="component">The LiteBus store component name.</param>
    /// <param name="schemaName">The unquoted schema name of the store table.</param>
    /// <param name="tableName">The unquoted table name of the store table.</param>
    /// <param name="version">The schema version to record.</param>
    /// <param name="appliedAt">The timestamp stored with the version row.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the version row is written.</returns>
    public static Task SetVersionAsync(
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
        var context = PostgreSqlSchemaOperationContext.ForVersionLookup(
            connection,
            options,
            component,
            PostgreSqlTableReference.Create(schemaName, tableName),
            logger);

        return SetVersionAsync(context, version, appliedAt, cancellationToken);
    }

    /// <summary>
    ///     Returns the SQL script that creates the schema version metadata table.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered metadata create SQL batch.</returns>
    public static string GetMetadataCreateScript(IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataCreate,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }

    /// <summary>
    ///     Returns the SQL script that reads one recorded schema version row.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered metadata select SQL batch.</returns>
    internal static string GetMetadataSelectVersionScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataSelectVersion,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }

    /// <summary>
    ///     Returns the SQL script that inserts or updates one recorded schema version row.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered metadata upsert SQL batch.</returns>
    internal static string GetMetadataUpsertVersionScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSchemaEmbeddedSql.MetadataUpsertVersion,
            PostgreSqlSchemaSqlTokens.ForMetadata(options));
    }
}