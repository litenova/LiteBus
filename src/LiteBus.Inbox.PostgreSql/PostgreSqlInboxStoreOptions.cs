using LiteBus.PostgreSql;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Defines PostgreSQL command inbox store and schema bootstrap options.
/// </summary>
public sealed record PostgreSqlInboxStoreOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name that stores inbox commands.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores inbox commands.
    /// </summary>
    public string TableName { get; init; } = "litebus_inbox_commands";

    /// <summary>
    ///     Gets a value indicating whether the application host should create or upgrade the inbox schema on startup.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, register <c>AddPostgreSqlCommandInboxSchemaHosting</c> from
    ///     <c>LiteBus.Inbox.PostgreSql.Extensions.Microsoft.Hosting</c>
    ///     so schema creation runs before inbox processing starts. Production systems that use Flyway, Liquibase, or EF
    ///     migrations should leave this <see langword="false" /> and apply the canonical SQL files from
    ///     <see cref="PostgreSqlInboxSchema.SqlFiles" /> or scripts from
    ///     <see cref="PostgreSqlInboxSchema.GetCreateScript(PostgreSqlInboxStoreOptions?)" />.
    /// </remarks>
    public bool EnsureSchemaCreationOnStartup { get; init; }

    /// <summary>
    ///     Gets a value indicating whether startup should fail when the inbox table does not match
    ///     <see cref="PostgreSqlInboxSchema.CurrentSchemaVersion" />.
    /// </summary>
    /// <remarks>
    ///     Validation runs after <see cref="EnsureSchemaCreationOnStartup" /> when both options are enabled. Manual callers
    ///     can invoke
    ///     <see cref="PostgreSqlInboxSchema.ValidateAsync(Npgsql.NpgsqlDataSource, PostgreSqlInboxStoreOptions?, System.Threading.CancellationToken)" />
    ///     directly during deploy checks.
    /// </remarks>
    public bool ValidateSchemaCreationOnStartup { get; init; } = true;
}
