using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Exceptions;
using Npgsql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL audit schema used by
///     <see cref="PostgreSqlAuditChainStore{TDocument}" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlAuditStoreOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlAuditStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlAuditStoreOptions.EnsureSchemaCreationOnStartup" /> to
///                 <see langword="true" /> and register the PostgreSQL audit module.
///             </description>
///         </item>
///     </list>
///     <para>
///         The audit schema uses version 1. Its records table holds staged and linked documents keyed by document
///         identity, its heads table holds one row per chain segment with the conditional-write version, and its
///         checkpoints table holds stored checkpoints with their publish state.
///     </para>
///     <para>
///         Bootstrap runs inside this package rather than through the shared schema manager because the shared
///         manager is internal to <c>LiteBus.Storage.PostgreSql</c> and that package does not grant this package
///         friend access yet. The mechanics mirror the shared ones exactly: the same advisory lock derivation, the
///         same version metadata rows in the shared metadata table, and the same drift messages.
///     </para>
/// </remarks>
public static class PostgreSqlAuditSchema
{
    /// <summary>
    ///     Gets the audit tables schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Gets the component name recorded in the schema version metadata table.
    /// </summary>
    public const string ComponentName = "audit";

    /// <summary>
    ///     The maximum time to wait for another session to finish schema bootstrap.
    /// </summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     The delay between checks while waiting for another session to finish schema bootstrap.
    /// </summary>
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Gets the canonical SQL files shipped with the audit PostgreSQL package.
    /// </summary>
    /// <remarks>
    ///     Paths are relative to the repository root, for example
    ///     <c>src/LiteBus.Auditing.Storage.PostgreSql/Sql/audit/v1/create.sql</c>. Replace
    ///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names before running the
    ///     scripts manually, or call <see cref="GetCreateScript(PostgreSqlAuditStoreOptions?)" /> to render them.
    /// </remarks>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlAuditSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current audit tables, indexes, and metadata table.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create the <c>litebus_audit_*</c> tables.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlAuditStoreOptions? options = null)
    {
        return PostgreSqlAuditSchemaScripts.GetCreateScript(options);
    }

    /// <summary>
    ///     Creates the audit schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    /// <remarks>
    ///     The operation is idempotent and safe to run from multiple application instances. One instance acquires a
    ///     PostgreSQL advisory lock while creating the schema; the others wait until the schema reaches the expected
    ///     version.
    /// </remarks>
    public static async Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlAuditStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlAuditStoreOptions();
        options.Validate();

        var logger = options.Logger ?? NullPostgreSqlSchemaLogger.Instance;
        var lockKey = PostgreSqlAuditSchemaScripts.CreateLockKey(options);

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Ensuring {ComponentName} schema creation for '{options.SchemaName}.{options.TableName}'.");

        using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var (key1, key2) = CreateLockKeys(lockKey);

        if (await TryAcquireLockAsync(connection, key1, key2, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await ApplyEnsureAsync(connection, options, logger, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseLockAsync(connection, key1, key2).ConfigureAwait(false);
            }
        }
        else
        {
            await WaitForExpectedVersionAsync(connection, options, logger, cancellationToken).ConfigureAwait(false);
        }

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema creation complete for {ComponentName} table '{options.SchemaName}.{options.TableName}'.");
    }

    /// <summary>
    ///     Validates that the audit tables match <see cref="CurrentSchemaVersion" />.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    /// <exception cref="PostgreSqlSchemaDriftException">
    ///     Thrown when a table is missing, incomplete, or recorded at an unexpected schema version.
    /// </exception>
    public static async Task ValidateAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlAuditStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlAuditStoreOptions();
        options.Validate();

        var logger = options.Logger ?? NullPostgreSqlSchemaLogger.Instance;

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Validating {ComponentName} schema for '{options.SchemaName}.{options.TableName}'.");

        using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var target in CreateTargets(options))
        {
            await ValidateTableAsync(connection, options, target, logger, cancellationToken).ConfigureAwait(false);
        }

        logger.Log(
            PostgreSqlSchemaLogLevel.Information,
            $"Schema validation succeeded for {ComponentName} table '{options.SchemaName}.{options.TableName}'.");
    }

    /// <summary>
    ///     Creates the schema when missing, ensures indexes, and records version metadata while holding the advisory
    ///     lock.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection holding the advisory lock.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when bootstrap finishes.</returns>
    private static async Task ApplyEnsureAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        await ExecuteScriptAsync(
                connection,
                PostgreSqlAuditSchemaScripts.BuildMetadataCreateScript(options),
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteScriptAsync(
                connection,
                PostgreSqlAuditSchemaScripts.BuildBaselineCreateScript(options),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var target in CreateTargets(options))
        {
            await ThrowWhenTableDriftedAsync(connection, target, logger, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteScriptAsync(
                connection,
                PostgreSqlAuditSchemaScripts.BuildEnsureIndexesScript(options),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var target in CreateTargets(options))
        {
            await SetRecordedVersionAsync(
                    connection,
                    options,
                    target.Table.TableName,
                    CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Waits until another session finishes schema bootstrap or the wait times out.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    private static async Task WaitForExpectedVersionAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        var lockKey = PostgreSqlAuditSchemaScripts.CreateLockKey(options);

        logger.Log(
            PostgreSqlSchemaLogLevel.Debug,
            $"Advisory lock '{lockKey}' is held by another session. Waiting for schema version {CurrentSchemaVersion}.");

        var deadline = DateTime.UtcNow + LockTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsAtExpectedVersionAsync(connection, options, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var (key1, key2) = CreateLockKeys(lockKey);

            if (await TryAcquireLockAsync(connection, key1, key2, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ApplyEnsureAsync(connection, options, logger, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await ReleaseLockAsync(connection, key1, key2).ConfigureAwait(false);
                }

                return;
            }

            await Task.Delay(LockPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new PostgreSqlStorageTimeoutException(
            $"Timed out after {LockTimeout} waiting for {ComponentName} schema " +
            $"'{options.SchemaName}.{options.TableName}' to reach version {CurrentSchemaVersion}.");
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the audit tables already match the expected schema version.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>
    ///     <see langword="true" /> when metadata or table shape indicate the expected version; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static async Task<bool> IsAtExpectedVersionAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var target in CreateTargets(options))
        {
            if (!await TableExistsAsync(connection, target.Table, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var columns = await GetColumnDataTypesAsync(connection, target.Table, cancellationToken)
                .ConfigureAwait(false);

            if (FindMissingColumns(columns, target.RequiredColumns).Count > 0 ||
                FindTypeMismatches(columns).Count > 0)
            {
                return false;
            }

            var recorded = await GetRecordedVersionAsync(
                    connection,
                    options,
                    target.Table.TableName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (recorded != 0 && recorded != CurrentSchemaVersion)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Validates one audit table shape and recorded schema version against the current package release.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="target">The table and its required columns and indexes.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    private static async Task ValidateTableAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        AuditTableTarget target,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, target.Table, cancellationToken).ConfigureAwait(false))
        {
            throw Drift(
                target,
                null,
                $"Table '{target.Table.QualifiedName}' does not exist.",
                logger);
        }

        var columns = await GetColumnDataTypesAsync(connection, target.Table, cancellationToken)
            .ConfigureAwait(false);
        var missingColumns = FindMissingColumns(columns, target.RequiredColumns);

        if (missingColumns.Count > 0)
        {
            throw Drift(
                target,
                null,
                $"Missing columns: {string.Join(", ", missingColumns)}.",
                logger);
        }

        var mismatches = FindTypeMismatches(columns);

        if (mismatches.Count > 0)
        {
            throw Drift(
                target,
                null,
                $"Column type mismatches: {string.Join(", ", mismatches)}.",
                logger);
        }

        var recorded = await GetRecordedVersionAsync(
                connection,
                options,
                target.Table.TableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (recorded != 0 && recorded != CurrentSchemaVersion)
        {
            throw Drift(
                target,
                recorded,
                "The recorded schema version does not match the current LiteBus schema contract. LiteBus does not " +
                "mutate incompatible tables automatically. Drain and replace the table, or run a reviewed " +
                "application-owned data migration before startup.",
                logger);
        }

        if (options.ValidateIndexesOnStartup)
        {
            List<string> missingIndexes = [];

            foreach (var indexName in target.RequiredIndexNames)
            {
                if (!await IndexExistsAsync(connection, target.Table, indexName, cancellationToken)
                        .ConfigureAwait(false))
                {
                    missingIndexes.Add(indexName);
                }
            }

            if (missingIndexes.Count > 0)
            {
                throw Drift(
                    target,
                    null,
                    $"Missing indexes: {string.Join(", ", missingIndexes)}.",
                    logger);
            }
        }
    }

    /// <summary>
    ///     Fails bootstrap when an existing audit table does not match the current column contract.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection holding the advisory lock.</param>
    /// <param name="target">The table and its required columns and indexes.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>A task that completes when the table matches.</returns>
    private static async Task ThrowWhenTableDriftedAsync(
        NpgsqlConnection connection,
        AuditTableTarget target,
        IPostgreSqlSchemaLogger logger,
        CancellationToken cancellationToken)
    {
        var columns = await GetColumnDataTypesAsync(connection, target.Table, cancellationToken)
            .ConfigureAwait(false);
        var missingColumns = FindMissingColumns(columns, target.RequiredColumns);

        if (missingColumns.Count > 0)
        {
            throw Drift(
                target,
                null,
                "The existing table does not match the current LiteBus schema version 1 column set. LiteBus does " +
                "not mutate incompatible tables automatically. Drain and replace the table, or run a reviewed " +
                "application-owned data migration before startup.",
                logger);
        }

        var mismatches = FindTypeMismatches(columns);

        if (mismatches.Count > 0)
        {
            throw Drift(
                target,
                null,
                $"Column type mismatches: {string.Join(", ", mismatches)}.",
                logger);
        }
    }

    /// <summary>
    ///     Builds the drift exception for one audit table and logs it.
    /// </summary>
    /// <param name="target">The table that failed validation.</param>
    /// <param name="actualVersion">The schema version recorded in metadata, if any.</param>
    /// <param name="details">Additional validation details.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <returns>The drift exception.</returns>
    private static PostgreSqlSchemaDriftException Drift(
        AuditTableTarget target,
        int? actualVersion,
        string details,
        IPostgreSqlSchemaLogger logger)
    {
        var exception = new PostgreSqlSchemaDriftException(
            ComponentName,
            target.Table.SchemaName,
            target.Table.TableName,
            CurrentSchemaVersion,
            actualVersion,
            details);

        logger.Log(PostgreSqlSchemaLogLevel.Error, exception.Message, exception);

        return exception;
    }

    /// <summary>
    ///     Creates the table targets validated and versioned by audit schema bootstrap.
    /// </summary>
    /// <param name="options">The schema and table options.</param>
    /// <returns>The records, heads, and checkpoints targets.</returns>
    private static IReadOnlyList<AuditTableTarget> CreateTargets(PostgreSqlAuditStoreOptions options)
    {
        return
        [
            new AuditTableTarget(
                PostgreSqlTableReference.Create(options.SchemaName, options.TableName),
                PostgreSqlAuditSchemaScripts.Version1RecordsColumns,
                [
                    PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "sequence_idx"),
                    PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "unlinked_idx"),
                    PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "tenant_idx")
                ]),
            new AuditTableTarget(
                PostgreSqlTableReference.Create(options.SchemaName, options.HeadsTableName),
                PostgreSqlAuditSchemaScripts.Version1HeadsColumns,
                [
                    PostgreSqlIdentifier.UnquotedIndexName(options.HeadsTableName, "tenant_idx"),
                    PostgreSqlIdentifier.UnquotedIndexName(options.HeadsTableName, "opened_idx")
                ]),
            new AuditTableTarget(
                PostgreSqlTableReference.Create(options.SchemaName, options.CheckpointsTableName),
                PostgreSqlAuditSchemaScripts.Version1CheckpointsColumns,
                [
                    PostgreSqlIdentifier.UnquotedIndexName(options.CheckpointsTableName, "segment_idx"),
                    PostgreSqlIdentifier.UnquotedIndexName(options.CheckpointsTableName, "unpublished_idx")
                ])
        ];
    }

    /// <summary>
    ///     Finds the required columns a table does not carry.
    /// </summary>
    /// <param name="columns">The column data types read from the table, keyed by column name.</param>
    /// <param name="requiredColumns">The required column names.</param>
    /// <returns>The required columns the table does not carry.</returns>
    private static List<string> FindMissingColumns(
        IReadOnlyDictionary<string, string> columns,
        IReadOnlyList<string> requiredColumns)
    {
        List<string> missing = [];

        foreach (var required in requiredColumns)
        {
            if (!columns.ContainsKey(required))
            {
                missing.Add(required);
            }
        }

        return missing;
    }

    /// <summary>
    ///     Finds the columns whose database type is not part of the current schema contract.
    /// </summary>
    /// <param name="columns">The column data types read from the table, keyed by column name.</param>
    /// <returns>One entry per mismatched column.</returns>
    private static List<string> FindTypeMismatches(IReadOnlyDictionary<string, string> columns)
    {
        List<string> mismatches = [];

        foreach (var (column, expected) in PostgreSqlAuditSchemaScripts.RequiredColumnDataTypes)
        {
            if (columns.TryGetValue(column, out var actual) &&
                !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"column '{column}' (expected {expected}, found {actual})");
            }
        }

        return mismatches;
    }

    /// <summary>
    ///     Derives a stable two-part advisory lock key from a logical lock name.
    /// </summary>
    /// <param name="lockKey">The logical lock name supplied by schema bootstrap code.</param>
    /// <returns>The key pair passed to PostgreSQL advisory lock functions.</returns>
    /// <remarks>
    ///     The derivation matches the shared advisory lock scopes, so the audit bootstrap contends correctly with
    ///     any other LiteBus bootstrap that names the same key.
    /// </remarks>
    private static (int Key1, int Key2) CreateLockKeys(string lockKey)
    {
        const string key2Seed = "\u0000litebus:advisory:key2";
        var key1 = (int) (PostgreSqlIdentifier.StableHash(lockKey) & 0x7FFFFFFF);
        var key2 = (int) (PostgreSqlIdentifier.StableHash(lockKey + key2Seed) & 0x7FFFFFFF);
        return (key1, key2);
    }

    /// <summary>
    ///     Attempts to acquire a session advisory lock for the supplied keys.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="key1">The first advisory lock key.</param>
    /// <param name="key2">The second advisory lock key.</param>
    /// <param name="cancellationToken">A token used to cancel the attempt.</param>
    /// <returns>
    ///     <see langword="true" /> when acquisition succeeded; otherwise, <see langword="false" />.
    /// </returns>
    private static async Task<bool> TryAcquireLockAsync(
        NpgsqlConnection connection,
        int key1,
        int key2,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key1, @key2);";
        command.Parameters.AddWithValue("key1", key1);
        command.Parameters.AddWithValue("key2", key2);

        return (bool) (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    /// <summary>
    ///     Releases a session advisory lock held for the supplied keys.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection holding the lock.</param>
    /// <param name="key1">The first advisory lock key.</param>
    /// <param name="key2">The second advisory lock key.</param>
    /// <returns>A task that completes when the lock is released.</returns>
    private static async Task ReleaseLockAsync(NpgsqlConnection connection, int key1, int key2)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key1, @key2);";
        command.Parameters.AddWithValue("key1", key1);
        command.Parameters.AddWithValue("key2", key2);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns whether a table exists as a base table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="table">The table to look up.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns><see langword="true" /> when the table exists; otherwise, <see langword="false" />.</returns>
    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        PostgreSqlTableReference table,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT EXISTS (SELECT 1
                                          FROM information_schema.tables
                                          WHERE table_schema = @schemaName
                                            AND table_name = @tableName
                                            AND table_type = 'BASE TABLE');
                           """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("schemaName", table.SchemaName);
        command.Parameters.AddWithValue("tableName", table.TableName);

        return (bool) (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    /// <summary>
    ///     Reads the column names and data types of a table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="table">The table to inspect.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The data types keyed by column name.</returns>
    private static async Task<IReadOnlyDictionary<string, string>> GetColumnDataTypesAsync(
        NpgsqlConnection connection,
        PostgreSqlTableReference table,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT column_name, data_type
                           FROM information_schema.columns
                           WHERE table_schema = @schemaName
                             AND table_name = @tableName;
                           """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("schemaName", table.SchemaName);
        command.Parameters.AddWithValue("tableName", table.TableName);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    /// <summary>
    ///     Returns whether an index exists on a table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="table">The table carrying the index.</param>
    /// <param name="indexName">The unquoted index name as stored in <c>pg_indexes.indexname</c>.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns><see langword="true" /> when the index exists; otherwise, <see langword="false" />.</returns>
    private static async Task<bool> IndexExistsAsync(
        NpgsqlConnection connection,
        PostgreSqlTableReference table,
        string indexName,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT EXISTS (SELECT 1
                                          FROM pg_indexes
                                          WHERE schemaname = @schemaName
                                            AND tablename = @tableName
                                            AND indexname = @indexName);
                           """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("schemaName", table.SchemaName);
        command.Parameters.AddWithValue("tableName", table.TableName);
        command.Parameters.AddWithValue("indexName", indexName);

        return (bool) (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    /// <summary>
    ///     Returns the recorded schema version, or <c>0</c> when no row exists or the metadata table is missing.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="tableName">The unquoted table name of the store table.</param>
    /// <param name="cancellationToken">A token used to cancel the lookup.</param>
    /// <returns>The recorded schema version, or <c>0</c> when no version row exists.</returns>
    private static async Task<int> GetRecordedVersionAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        string tableName,
        CancellationToken cancellationToken)
    {
        var metadataTable = PostgreSqlTableReference.ForMetadata(options);

        if (!await TableExistsAsync(connection, metadataTable, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var sql = $"""
                   SELECT version
                   FROM {metadataTable.QuotedQualifiedName}
                   WHERE component = @component
                     AND schema_name = @schemaName
                     AND table_name = @tableName;
                   """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", ComponentName);
        command.Parameters.AddWithValue("schemaName", options.SchemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        return ConvertVersionResult(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    ///     Records the applied schema version for one audit table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="tableName">The unquoted table name of the store table.</param>
    /// <param name="version">The schema version to record.</param>
    /// <param name="appliedAt">The timestamp stored with the version row.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the version row is written.</returns>
    private static async Task SetRecordedVersionAsync(
        NpgsqlConnection connection,
        PostgreSqlAuditStoreOptions options,
        string tableName,
        int version,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        var metadataTable = PostgreSqlTableReference.ForMetadata(options);
        var sql = $"""
                   INSERT INTO {metadataTable.QuotedQualifiedName} (component, schema_name, table_name, version, applied_at)
                   VALUES (@component, @schemaName, @tableName, @version, @appliedAt)
                   ON CONFLICT (component, schema_name, table_name)
                       DO UPDATE SET version = EXCLUDED.version, applied_at = EXCLUDED.applied_at;
                   """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("component", ComponentName);
        command.Parameters.AddWithValue("schemaName", options.SchemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("appliedAt", appliedAt);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Converts a schema version scalar read from the metadata row to a version number.
    /// </summary>
    /// <param name="result">The scalar value read from the metadata row, or <see langword="null" /> when no row exists.</param>
    /// <returns>The recorded schema version, or <c>0</c> when no version row exists.</returns>
    /// <exception cref="DurableStorageConfigurationException">
    ///     The recorded value is not an <c>int</c>, <c>long</c>, <c>short</c>, or <c>decimal</c>.
    /// </exception>
    private static int ConvertVersionResult(object? result)
    {
        return result switch
        {
            null => 0,
            DBNull => 0,
            int version => version,
            short version => version,
            long version => checked((int) version),
            decimal version => checked((int) version),
            _ => throw new DurableStorageConfigurationException(
                $"Recorded schema version has unsupported type '{result!.GetType().FullName}'. " +
                "Expected an int, long, short, or decimal value.")
        };
    }

    /// <summary>
    ///     Executes one SQL batch on an open connection.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="sql">The SQL batch text.</param>
    /// <param name="cancellationToken">A token used to cancel the execution.</param>
    /// <returns>A task that completes when the batch runs.</returns>
    private static async Task ExecuteScriptAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     One audit table with the columns and indexes bootstrap validates.
    /// </summary>
    /// <param name="Table">The table reference.</param>
    /// <param name="RequiredColumns">The column names required by the current schema version.</param>
    /// <param name="RequiredIndexNames">The unquoted index names required by the current schema version.</param>
    private sealed record AuditTableTarget(
        PostgreSqlTableReference Table,
        IReadOnlyList<string> RequiredColumns,
        IReadOnlyList<string> RequiredIndexNames);
}
