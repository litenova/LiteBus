using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL saga schema used by <see cref="PostgreSqlSagaStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlSagaStoreOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlSagaStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlSagaStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> and
///                 register the PostgreSQL saga schema hosting module.
///             </description>
///         </item>
///     </list>
///     <para>
///         Schema version 2 adds the applied message identifier used for duplicate dispatch suppression. Existing
///         databases are not upgraded automatically. Apply the ordered files exposed by <see cref="SqlFiles" />, then
///         call <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlSagaStoreOptions?, CancellationToken)" /> to record and
///         validate the current version.
///     </para>
/// </remarks>
public static class PostgreSqlSagaSchema
{
    /// <summary>
    ///     Gets the saga table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    ///     Gets the canonical SQL files shipped with the saga PostgreSQL package.
    /// </summary>
    /// <remarks>
    ///     Paths are relative to the repository root, for example
    ///     <c>src/LiteBus.Saga.Storage.PostgreSql/Sql/saga/v1/create.sql</c>. Replace
    ///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names before running the
    ///     scripts manually, or call <see cref="GetCreateScript(PostgreSqlSagaStoreOptions?)" /> to render them.
    /// </remarks>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlSagaSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current saga table, indexes, and metadata table.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_saga_instances</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlSagaStoreOptions? options = null)
    {
        return PostgreSqlSagaSchemaScripts.GetCreateScript(options);
    }

    /// <summary>
    ///     Creates the saga schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    /// <remarks>
    ///     The operation is idempotent and safe to run from multiple application instances. One instance acquires a
    ///     PostgreSQL advisory lock while creating the schema; the others wait until the schema reaches the expected version.
    /// </remarks>
    public static Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlSagaStoreOptions();

        return PostgreSqlSchemaManager.EnsureAsync(
            dataSource,
            options,
            PostgreSqlSagaSchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Validates that the saga table matches <see cref="CurrentSchemaVersion" />.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    /// <exception cref="PostgreSqlSchemaDriftException">
    ///     Thrown when the table is missing, incomplete, or recorded at an unexpected schema version.
    /// </exception>
    public static Task ValidateAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlSagaStoreOptions();

        return PostgreSqlSchemaManager.ValidateAsync(
            dataSource,
            options,
            PostgreSqlSagaSchemaScripts.Definition,
            cancellationToken);
    }
}
