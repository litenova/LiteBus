using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL inbox schema used by <see cref="PostgreSqlInboxStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlInboxStoreOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlInboxStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlInboxStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> and
///                 register the PostgreSQL inbox schema hosting module.
///             </description>
///         </item>
///     </list>
///     <para>
///         Physical table schema version is tracked separately from message contract version stored on each row. Contract
///         version describes payload shape; table schema version describes columns and indexes managed by LiteBus.
///     </para>
///     <para>
///         Schema version 1 includes the full inbox column set, required indexes, and an optional insert notify trigger
///         for
///         LISTEN/NOTIFY wake-up. Existing databases are not upgraded; recreate tables or apply
///         <see cref="GetCreateScript(PostgreSqlInboxStoreOptions?)" /> through your migration pipeline.
///     </para>
/// </remarks>
public static class PostgreSqlInboxSchema
{
    /// <summary>
    ///     Gets the inbox table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Gets the canonical SQL files shipped with the inbox PostgreSQL package.
    /// </summary>
    /// <remarks>
    ///     Paths are relative to the repository root, for example
    ///     <c>src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/create.sql</c>. Replace
    ///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names before running the
    ///     scripts manually, or call <see cref="GetCreateScript(PostgreSqlInboxStoreOptions?)" /> to render them.
    /// </remarks>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlInboxSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the inbox schema version 1 table, indexes, metadata table, and notify trigger.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_inbox_messages</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    /// <example>
    ///     <code>
    /// var ddl = PostgreSqlInboxSchema.GetCreateScript(new PostgreSqlInboxStoreOptions
    /// {
    ///     SchemaName = "app",
    ///     TableName = "app_inbox"
    /// });
    /// File.WriteAllText("V001__litebus_inbox.sql", ddl);
    ///     </code>
    /// </example>
    public static string GetCreateScript(PostgreSqlInboxStoreOptions? options = null)
    {
        options ??= new PostgreSqlInboxStoreOptions();
        return PostgreSqlInboxSchemaScripts.BuildCreateScript(options);
    }

    /// <summary>
    ///     Creates the inbox schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    /// <remarks>
    ///     The operation is idempotent and safe to run from multiple application instances. One instance acquires a
    ///     PostgreSQL advisory lock while creating the schema; the others wait until the schema reaches the expected version.
    /// </remarks>
    /// <example>
    ///     <code>
    /// await PostgreSqlInboxSchema.EnsureAsync(dataSource, new PostgreSqlInboxStoreOptions
    /// {
    ///     SchemaName = "app",
    ///     Logger = mySchemaLogger
    /// }, cancellationToken);
    ///     </code>
    /// </example>
    public static Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlInboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlInboxStoreOptions();

        return PostgreSqlSchemaManager.EnsureAsync(
            dataSource,
            options,
            PostgreSqlInboxSchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Creates the inbox table and indexes when they do not exist.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_inbox_messages</c>.</param>
    /// <param name="cancellationToken">A token used to cancel the database command before it completes.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    /// <remarks>
    ///     Prefer <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlInboxStoreOptions?, CancellationToken)" /> for new
    ///     code. This method delegates to
    ///     <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlInboxStoreOptions?, CancellationToken)" />.
    /// </remarks>
    public static Task CreateIfNotExistsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlInboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureAsync(dataSource, options, cancellationToken);
    }

    /// <summary>
    ///     Validates that the inbox table matches <see cref="CurrentSchemaVersion" />.
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
        PostgreSqlInboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlInboxStoreOptions();

        return PostgreSqlSchemaManager.ValidateAsync(
            dataSource,
            options,
            PostgreSqlInboxSchemaScripts.Definition,
            cancellationToken);
    }
}