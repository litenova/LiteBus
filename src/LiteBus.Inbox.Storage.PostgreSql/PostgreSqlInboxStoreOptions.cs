using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Defines PostgreSQL inbox store and schema bootstrap options.
/// </summary>
public sealed record PostgreSqlInboxStoreOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions, IMessageStoreRetentionOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name that stores inbox messages.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores inbox messages.
    /// </summary>
    public string TableName { get; init; } = "litebus_inbox_messages";

    /// <summary>
    ///     Gets a value indicating whether the application host should create or upgrade the inbox schema on startup.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, <see cref="PostgreSqlInboxSchemaInitializer" /> creates or upgrades schema on host startup.
    ///     so schema creation runs before inbox processing starts. Production systems that use Flyway, Liquibase, or EF
    ///     migrations should set this to <see langword="false" /> and apply the canonical SQL files from
    ///     <see cref="PostgreSqlInboxSchema.SqlFiles" /> or scripts from
    ///     <see cref="PostgreSqlInboxSchema.GetCreateScript(PostgreSqlInboxStoreOptions?)" />.
    /// </remarks>
    public bool EnsureSchemaCreationOnStartup { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether startup should fail when the inbox table does not match
    ///     <see cref="PostgreSqlInboxSchema.CurrentSchemaVersion" />.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, <see cref="PostgreSqlInboxSchemaInitializer" /> validates the schema during host
    ///     startup even if <see cref="EnsureSchemaCreationOnStartup" /> is <see langword="false" />. Validation runs after
    ///     ensure when both options are enabled. Manual callers can invoke
    ///     <see cref="PostgreSqlInboxSchema.ValidateAsync(Npgsql.NpgsqlDataSource, PostgreSqlInboxStoreOptions?, System.Threading.CancellationToken)" />
    ///     directly during deploy checks.
    /// </remarks>
    public bool ValidateSchemaCreationOnStartup { get; init; } = true;

    /// <inheritdoc />
    public TimeSpan? TerminalRetention { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the inbox processor should listen for PostgreSQL
    ///     <c>NOTIFY</c> events after inserts, with polling as a fallback.
    /// </summary>
    public bool UseListenNotify { get; init; }
}
