using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.PostgreSql;

/// <summary>
///     Creates, upgrades, and validates LiteBus PostgreSQL store schemas.
/// </summary>
internal static class PostgreSqlSchemaManager
{
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultLockPollInterval = TimeSpan.FromMilliseconds(200);

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

        throw new TimeoutException(
            $"Timed out after {DefaultLockTimeout} waiting for {definition.Component} schema " +
            $"'{options.SchemaName}.{options.TableName}' to reach version {definition.CurrentSchemaVersion}.");
    }

    private static async Task ApplyEnsureAsync(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        await PostgreSqlSchemaVersionStore.EnsureMetadataTableAsync(connection, options, logger, cancellationToken)
            .ConfigureAwait(false);

        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(
                connection,
                options,
                definition.Component,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        var tableExists = await PostgreSqlSchemaInspector.TableExistsAsync(
                connection,
                options.SchemaName,
                options.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        var currentVersion = recordedVersion;

        if (tableExists)
        {
            var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                    connection,
                    options.SchemaName,
                    options.TableName,
                    cancellationToken)
                .ConfigureAwait(false);

            var inferredVersion = PostgreSqlSchemaInspector.InferVersionFromColumns(
                columns,
                definition.VersionColumnSets);

            if (inferredVersion < currentVersion)
            {
                currentVersion = inferredVersion;
            }
            else if (currentVersion == 0)
            {
                currentVersion = inferredVersion;
            }
        }

        if (currentVersion == 0 && !tableExists)
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
            currentVersion = 1;
        }

        while (currentVersion < definition.CurrentSchemaVersion)
        {
            var nextVersion = currentVersion + 1;

            logger.Log(
                PostgreSqlSchemaLogLevel.Information,
                $"Upgrading {definition.Component} schema from version {currentVersion} to {nextVersion} " +
                $"for '{options.SchemaName}.{options.TableName}'.");

            await PostgreSqlSchemaExecutor.ExecuteScriptAsync(
                    connection,
                    definition.BuildUpgradeScript(options, currentVersion, nextVersion),
                    logger,
                    cancellationToken)
                .ConfigureAwait(false);
            currentVersion = nextVersion;
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
                "Run EnsureAsync or apply the published upgrade scripts before starting the application.");

            logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }
    }

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
