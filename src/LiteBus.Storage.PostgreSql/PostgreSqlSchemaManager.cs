using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql.Exceptions;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Creates and validates LiteBus PostgreSQL store schemas at each component's current version.
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
        var storeTable = PostgreSqlTableReference.ForStore(options);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Ensuring {definition.Component} schema creation for '{storeTable.QualifiedName}'.");

        using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var context = PostgreSqlSchemaOperationContext.ForComponent(connection, options, definition, logger);
        await EnsureWithLockAsync(context, cancellationToken).ConfigureAwait(false);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema creation complete for {definition.Component} table '{storeTable.QualifiedName}'.");
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
        var storeTable = PostgreSqlTableReference.ForStore(options);

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Validating {definition.Component} schema for '{storeTable.QualifiedName}'.");

        using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var context = PostgreSqlSchemaOperationContext.ForComponent(connection, options, definition, logger);
        await ValidateCoreAsync(context, cancellationToken).ConfigureAwait(false);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema validation succeeded for {definition.Component} table '{storeTable.QualifiedName}'.");
    }

    /// <summary>
    ///     Applies schema bootstrap under an advisory lock or waits for another session to finish.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    private static async Task EnsureWithLockAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Schema ensure requires a component schema definition.");

        var lockKey = definition.CreateLockKey(context.Options);

        var lockScope = await PostgreSqlAdvisoryLockScope.TryAcquireAsync(
                context.Connection,
                lockKey,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (lockScope is not null)
            {
                context.Logger.Log(PostgreSqlSchemaLogLevel.Debug, $"Acquired advisory lock '{lockKey}'.");
                await ApplyEnsureAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            context.Logger.Log(
                PostgreSqlSchemaLogLevel.Debug,
                $"Advisory lock '{lockKey}' is held by another session. Waiting for schema version {definition.CurrentSchemaVersion}.");

            var deadline = DateTime.UtcNow + DefaultLockTimeout;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await IsAtExpectedVersionAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    context.Logger.Log(
                        PostgreSqlSchemaLogLevel.Debug,
                        $"Schema version {definition.CurrentSchemaVersion} is available without acquiring lock '{lockKey}'.");

                    return;
                }

                lockScope = await PostgreSqlAdvisoryLockScope.TryAcquireAsync(
                        context.Connection,
                        lockKey,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (lockScope is not null)
                {
                    context.Logger.Log(
                        PostgreSqlSchemaLogLevel.Debug,
                        $"Acquired advisory lock '{lockKey}' after waiting for another session.");

                    await ApplyEnsureAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(DefaultLockPollInterval, cancellationToken).ConfigureAwait(false);
            }

            throw new PostgreSqlStorageTimeoutException(
                $"Timed out after {DefaultLockTimeout} waiting for {definition.Component} schema " +
                $"'{context.StoreTable.QualifiedName}' to reach version {definition.CurrentSchemaVersion}.");
        }
        finally
        {
            if (lockScope is not null)
            {
                await lockScope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Creates the schema when missing, ensures indexes, and records version metadata while holding the advisory lock.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when bootstrap finishes.</returns>
    private static async Task ApplyEnsureAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Schema ensure requires a component schema definition.");

        await PostgreSqlSchemaVersionStore.EnsureMetadataTableAsync(context, cancellationToken)
            .ConfigureAwait(false);

        var tableExists = await PostgreSqlSchemaInspector.TableExistsAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);

        if (!tableExists)
        {
            context.Logger.Log(
                PostgreSqlSchemaLogLevel.Information,
                $"Creating {definition.Component} schema version {definition.CurrentSchemaVersion} for " +
                $"'{context.StoreTable.QualifiedName}'.");

            await PostgreSqlSchemaExecutor.ExecuteScriptAsync(
                    context.Connection,
                    definition.BuildBaselineCreateScript(context.Options),
                    context.Logger,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);

        var inferredVersion = PostgreSqlSchemaInspector.InferVersionFromColumns(
            columns,
            definition.VersionColumnSets);

        if (inferredVersion < definition.CurrentSchemaVersion)
        {
            var details = tableExists && definition.CurrentSchemaVersion == 1
                ? "The existing table does not match the current LiteBus schema version 1 column set. LiteBus does " +
                  "not mutate incompatible tables automatically. Drain and replace the table, or run a reviewed " +
                  "application-owned data migration before startup."
                : $"Table column set infers schema version {inferredVersion}.";

            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                context.StoreTable.SchemaName,
                context.StoreTable.TableName,
                definition.CurrentSchemaVersion,
                inferredVersion > 0 ? inferredVersion : null,
                details);

            context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        await ValidateRequiredColumnDataTypesAsync(context, cancellationToken).ConfigureAwait(false);

        await PostgreSqlSchemaExecutor.ExecuteScriptAsync(
                context.Connection,
                definition.BuildEnsureIndexesScript(context.Options),
                context.Logger,
                cancellationToken)
            .ConfigureAwait(false);

        await PostgreSqlSchemaVersionStore.SetVersionAsync(
                context,
                definition.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates table shape and recorded schema version against the current package release.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    private static async Task ValidateCoreAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Schema validation requires a component schema definition.");

        var tableExists = await PostgreSqlSchemaInspector.TableExistsAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);

        if (!tableExists)
        {
            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                context.StoreTable.SchemaName,
                context.StoreTable.TableName,
                definition.CurrentSchemaVersion,
                null,
                $"Table '{context.StoreTable.QualifiedName}' does not exist.");

            context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                context.Connection,
                context.StoreTable,
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
                context.StoreTable.SchemaName,
                context.StoreTable.TableName,
                definition.CurrentSchemaVersion,
                null,
                $"Missing columns: {string.Join(", ", missingColumns)}.");

            context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        await ValidateRequiredColumnDataTypesAsync(context, cancellationToken).ConfigureAwait(false);

        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(context, cancellationToken)
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
                    context.StoreTable.SchemaName,
                    context.StoreTable.TableName,
                    definition.CurrentSchemaVersion,
                    inferredVersion == 0 ? null : inferredVersion,
                    "Schema metadata is missing and the table shape does not match the current LiteBus release.");

                context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
                throw exception;
            }

            return;
        }

        if (recordedVersion != definition.CurrentSchemaVersion)
        {
            var exception = new PostgreSqlSchemaDriftException(
                definition.Component,
                context.StoreTable.SchemaName,
                context.StoreTable.TableName,
                definition.CurrentSchemaVersion,
                recordedVersion,
                "The recorded schema version does not match the current LiteBus schema contract. LiteBus does not " +
                "mutate incompatible tables automatically. Drain and replace the table, or run a reviewed " +
                "application-owned data migration before startup.");

            context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
            throw exception;
        }

        if (ShouldValidateIndexes(context.Options))
        {
            await ValidateRequiredIndexesAsync(context, cancellationToken)
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
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    private static async Task ValidateRequiredIndexesAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Index validation requires a component schema definition.");

        List<string> missingIndexes = [];

        foreach (var indexName in definition.GetRequiredIndexNames(context.Options))
        {
            if (!await PostgreSqlSchemaInspector.IndexExistsAsync(
                        context.Connection,
                        context.StoreTable,
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
            context.StoreTable.SchemaName,
            context.StoreTable.TableName,
            definition.CurrentSchemaVersion,
            null,
            $"Missing indexes: {string.Join(", ", missingIndexes)}.");

        context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
        throw exception;
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the store table already matches the expected schema version.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>
    ///     <see langword="true" /> when metadata or inferred columns indicate the expected version; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static async Task<bool> IsAtExpectedVersionAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Version checks require a component schema definition.");

        if (!await PostgreSqlSchemaInspector.TableExistsAsync(
                    context.Connection,
                    context.StoreTable,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        var columns = await PostgreSqlSchemaInspector.GetColumnNamesAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);

        var hasExpectedColumns = PostgreSqlSchemaInspector.InferVersionFromColumns(
            columns,
            definition.VersionColumnSets) >= definition.CurrentSchemaVersion;

        if (!hasExpectedColumns)
        {
            return false;
        }

        var recordedVersion = await PostgreSqlSchemaVersionStore.GetVersionAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (recordedVersion != 0 && recordedVersion != definition.CurrentSchemaVersion)
        {
            return false;
        }

        return await HasRequiredColumnDataTypesAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates database types that are part of the current component schema contract.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>A task that completes when the required column types match.</returns>
    private static async Task ValidateRequiredColumnDataTypesAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Column type validation requires a component schema definition.");
        var actualDataTypes = await PostgreSqlSchemaInspector.GetColumnDataTypesAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);
        var mismatches = PostgreSqlSchemaInspector.GetColumnDataTypeMismatches(
            actualDataTypes,
            definition.RequiredColumnDataTypes);

        if (mismatches.Count == 0)
        {
            return;
        }

        var exception = new PostgreSqlSchemaDriftException(
            definition.Component,
            context.StoreTable.SchemaName,
            context.StoreTable.TableName,
            definition.CurrentSchemaVersion,
            null,
            $"Column type mismatches: {string.Join(", ", mismatches)}.");

        context.Logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);
        throw exception;
    }

    /// <summary>
    ///     Returns whether database types that are part of the current component schema contract match.
    /// </summary>
    /// <param name="context">The schema operation context.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns><see langword="true" /> when every required type matches; otherwise, <see langword="false" />.</returns>
    private static async Task<bool> HasRequiredColumnDataTypesAsync(
        PostgreSqlSchemaOperationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Definition ?? throw new InvalidOperationException("Column type validation requires a component schema definition.");
        var actualDataTypes = await PostgreSqlSchemaInspector.GetColumnDataTypesAsync(
                context.Connection,
                context.StoreTable,
                cancellationToken)
            .ConfigureAwait(false);

        return PostgreSqlSchemaInspector.GetColumnDataTypeMismatches(
            actualDataTypes,
            definition.RequiredColumnDataTypes).Count == 0;
    }
}
