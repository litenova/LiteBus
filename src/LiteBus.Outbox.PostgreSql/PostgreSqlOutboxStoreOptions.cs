using LiteBus.PostgreSql;

namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Defines PostgreSQL outbox store and schema bootstrap options.
/// </summary>
public sealed record PostgreSqlOutboxStoreOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name that stores outbox messages.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores outbox messages.
    /// </summary>
    public string TableName { get; init; } = "litebus_outbox_messages";

    /// <summary>
    ///     Gets a value indicating whether the application host should create or upgrade the outbox schema on startup.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, register
    ///     <see cref="Extensions.Microsoft.Hosting.ModuleRegistryHostingExtensions.AddPostgreSqlOutboxSchemaHosting(LiteBus.Runtime.Abstractions.IModuleRegistry)" />
    ///     so schema creation runs before outbox processing starts. Production systems that use Flyway, Liquibase, or EF
    ///     migrations should leave this <see langword="false" /> and apply the canonical SQL files from
    ///     <see cref="PostgreSqlOutboxSchema.SqlFiles" /> or scripts from
    ///     <see cref="PostgreSqlOutboxSchema.GetCreateScript(PostgreSqlOutboxStoreOptions?)" />.
    /// </remarks>
    public bool EnsureSchemaCreationOnStartup { get; init; }

    /// <summary>
    ///     Gets a value indicating whether startup should fail when the outbox table does not match
    ///     <see cref="PostgreSqlOutboxSchema.CurrentSchemaVersion" />.
    /// </summary>
    /// <remarks>
    ///     Validation runs after <see cref="EnsureSchemaCreationOnStartup" /> when both options are enabled. Manual callers
    ///     can invoke
    ///     <see cref="PostgreSqlOutboxSchema.ValidateAsync(Npgsql.NpgsqlDataSource, PostgreSqlOutboxStoreOptions?, System.Threading.CancellationToken)" />
    ///     directly during deploy checks.
    /// </remarks>
    public bool ValidateSchemaCreationOnStartup { get; init; } = true;
}
