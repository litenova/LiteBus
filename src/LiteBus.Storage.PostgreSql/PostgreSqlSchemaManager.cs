using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Creates and validates LiteBus PostgreSQL store schemas at schema version 1.
/// </summary>
internal static class PostgreSqlSchemaManager
{
    /// <summary>
    ///     The maximum time to wait for another session to finish schema bootstrap.
    /// </summary>
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     The delay between checks while waiting for another session to finish schema bootstrap.
    /// </summary>
    private static readonly TimeSpan DefaultLockPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Creates one LiteBus PostgreSQL store schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies SQL builders.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    internal static async Task EnsureAsync(
        NpgsqlDataSource dataSource,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(definition);

        var logger = options.Logger ?? NullPostgreSqlSchemaLogger.Instance;

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Ensuring {definition.Component} schema creation for '{options.SchemaName}.{options.TableName}'.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureWithLockAsync(connection, options, definition, logger, cancellationToken).ConfigureAwait(false);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema creation complete for {definition.Component} table '{options.SchemaName}.{options.TableName}'.");
    }

    /// <summary>
    ///     Validates that one LiteBus PostgreSQL store schema matches the current package version.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies validation metadata.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    internal static async Task ValidateAsync(
        NpgsqlDataSource dataSource,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(definition);

        var logger = options.Logger ?? NullPostgreSqlSchemaLogger.Instance;

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Validating {definition.Component} schema for '{options.SchemaName}.{options.TableName}'.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ValidateCoreAsync(connection, options, definition, logger, cancellationToken).ConfigureAwait(false);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema validation succeeded for {definition.Component} table '{options.SchemaName}.{options.TableName}'.");
    }

    /// <summary>
    ///     Applies schema bootstrap under an advisory lock or waits for another session to finish.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies SQL builders.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    private static async Task EnsureWithLockAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        var lockKey = definition.CreateLockKey(options);

        await using var lockScope = await PostgreSqlAdvisoryLockScope.TryAcquireAsync(
                connection,
                lockKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (lockScope is not null)
        {
            logger.Log(PostgreSqlSchemaLogLevel.Debug, $"Acquired advisory lock '{lockKey}'.");
            await ApplyEnsureAsync(connection, options, definition, logger, cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Advisory lock '{lockKey}' is held by another session. Waiting for schema version {definition.CurrentSchemaVersion}.");

        var deadline = DateTime.UtcNow + DefaultLockTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsAtExpectedVersionAsync(connection, options, definition, cancellationToken).ConfigureAwait(false))
            {
                logger.Log(
                    PostgreSqlSchemaLogLevel.Debug,
                    $"Schema version {definition.CurrentSchemaVersion} is available without acquiring lock '{lockKey}'.");
                return;
            }

            await Task.Delay(DefaultLockPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new Exceptions.PostgreSqlStorageTimeoutException(
            $"Timed out after {DefaultLockTimeout} waiting for {definition.Component} schema " +
            $"'{options.SchemaName}.{options.TableName}' to reach version {definition.CurrentSchemaVersion}.");
    }

    /// <summary>
    ///     Creates the schema when missing, ensures indexes, and records version metadata while holding the advisory lock.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies SQL builders.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when bootstrap finishes.</returns>
    private static async Task ApplyEnsureAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        await PostgreSqlSchemaVersionStore.EnsureMetadataTableAsync(connection, options, logger, cancellationToken)
            .ConfigureAwait(false);

        var tableExists = await PostgreSqlSchemaInspector.TableExistsAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (!tableExists)
        {
            logger.Log(
                PostgreSqlSchemaLogLevel.Information,
                $"Creating {definition.Component} schema version 1 for '{options.SchemaName}.{options.TableName}'.");

            await PostgreSqlSchemaExecutor.ExecuteScriptAsync(
                    connection,
                    definition.BuildVersion1CreateScript(options),
                    logger,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        var inferredVersion = PostgreSqlSchemaInspector.InferVersionFromColumns(
            columns,
            definition.VersionColumnSets);

        if (inferredVersion < definition.CurrentSchemaVersion)
        {
            if (tableExists)
            {
                logger.Log(
                    PostgreSqlSchemaLogLevel.Warning,
                    $"{definition.Component} table '{options.SchemaName}.{options.TableName}' exists but does not " +
                    $"match schema version {definition.CurrentSchemaVersion}. Recreate the table or apply " +
                    "GetCreateScript() through your migration pipeline.");
            }

            return;
        }

        await PostgreSqlSchemaExecutor.ExecuteScriptAsync(
                connection,
                definition.BuildEnsureIndexesScript(options),
                logger,
                cancellationToken)
            .ConfigureAwait(false);

        await PostgreSqlSchemaVersionStore.SetVersionAsync(
                connection,
                options,
                definition.Component,
                options.SchemaName,
                options.TableName,
                definition.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                logger,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates table shape and recorded schema version against the current package release.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies validation metadata.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    private static async Task ValidateCoreAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        var tableExists = await PostgreSqlSchemaInspector.TableExistsAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (!tableExists)
        {
            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                options.SchemaName,
                options.TableName,
                definition.CurrentSchemaVersion,
                actualVersion: null,
                $"Table '{options.SchemaName}.{options.TableName}' does not exist.");

            logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        PostgreSqlSchemaInspector.ValidateRequiredColumns(
            columns,
            PostgreSqlSchemaInspector.GetRequiredColumns(definition.VersionColumnSets, definition.CurrentSchemaVersion),
            out var missingColumns);

        if (missingColumns.Count > 0)
        {
            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                options.SchemaName,
                options.TableName,
                definition.CurrentSchemaVersion,
                actualVersion: null,
                $"Missing columns: {string.Join(", ", missingColumns)}.");

            logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(
                connection,
                options,
                definition.Component,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (recordedVersion == 0)
        {
            var inferredVersion = PostgreSqlSchemaInspector.InferVersionFromColumns(
                columns,
                definition.VersionColumnSets);

            if (inferredVersion != definition.CurrentSchemaVersion)
            {
                var exception = new PostgreSqlSchemaDriftException(
                    definition.Component,
                    options.SchemaName,
                    options.TableName,
                    definition.CurrentSchemaVersion,
                    actualVersion: inferredVersion == 0 ? null : inferredVersion,
                    "Schema metadata is missing and the table shape does not match the current LiteBus release.");

                logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
                throw exception;
            }

            return;
        }

        if (recordedVersion != definition.CurrentSchemaVersion)
        {
            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                options.SchemaName,
                options.TableName,
                definition.CurrentSchemaVersion,
                recordedVersion,
                "Run EnsureAsync or apply GetCreateScript() before starting the application.");

            logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        if (ShouldValidateIndexes(options))
        {
            await ValidateRequiredIndexesAsync(connection, options, definition, logger, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Returns <see langword="true" /> when startup validation should verify required indexes on the store table.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns><see langword="true" /> when index validation is enabled; otherwise, <see langword="false" />.</returns>
    private static bool ShouldValidateIndexes(IPostgreSqlStoreTableOptions options)
    {
        return options is PostgreSqlSchemaStoreOptions storeOptions
            ? storeOptions.ValidateIndexesOnStartup
            : true;
    }

    /// <summary>
    ///     Validates that all required indexes for the current schema version exist on the store table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies index names.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    private static async Task ValidateRequiredIndexesAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        var missingIndexes = new List<string>();

        foreach (var indexName in definition.GetRequiredIndexNames(options))
        {
            if (!await PostgreSqlSchemaInspector.IndexExistsAsync(
                    connection,
                    options.SchemaName,
                    options.TableName,
                    indexName,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                missingIndexes.Add(indexName);
            }
        }

        if (missingIndexes.Count == 0)
        {
            return;
        }

        var exception = new PostgreSqlSchemaDriftException(
            definition.Component,
            options.SchemaName,
            options.TableName,
            definition.CurrentSchemaVersion,
            actualVersion: null,
            $"Missing indexes: {string.Join(", ", missingIndexes)}.");

        logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
        throw exception;
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the store table already matches the expected schema version.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies validation metadata.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>
    ///     <see langword="true" /> when metadata or inferred columns indicate the expected version; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static async Task<bool> IsAtExpectedVersionAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        CancellationToken cancellationToken)
    {
        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(
                connection,
                options,
                definition.Component,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (recordedVersion >= definition.CurrentSchemaVersion)
        {
            return true;
        }

        if (!await PostgreSqlSchemaInspector.TableExistsAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        return PostgreSqlSchemaInspector.InferVersionFromColumns(
                   columns,
                   definition.VersionColumnSets) >= definition.CurrentSchemaVersion;
    }
}
