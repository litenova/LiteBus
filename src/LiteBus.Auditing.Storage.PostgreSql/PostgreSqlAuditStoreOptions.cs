using System;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Defines PostgreSQL audit store and schema bootstrap options.
/// </summary>
/// <remarks>
///     The audit store keeps three tables in one schema: the records table holds staged and linked documents, the
///     heads table holds one row per chain segment, and the checkpoints table holds stored checkpoints with their
///     publish state. <see cref="TableName" /> names the records table, which is the primary store table the schema
///     version metadata tracks alongside the other two.
/// </remarks>
public sealed record PostgreSqlAuditStoreOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name that stores audit records, heads, and checkpoints.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores staged and linked audit documents.
    /// </summary>
    public string TableName { get; init; } = "litebus_audit_records";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores one head row per chain segment.
    /// </summary>
    public string HeadsTableName { get; init; } = "litebus_audit_heads";

    /// <summary>
    ///     Gets the PostgreSQL table name that stores checkpoints with their publish state.
    /// </summary>
    public string CheckpointsTableName { get; init; } = "litebus_audit_checkpoints";

    /// <summary>
    ///     Gets a value indicating whether the application host should create the audit schema on startup.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, <see cref="PostgreSqlAuditSchemaInitializer" /> creates the schema on host
    ///     startup, so schema creation runs before auditing starts. Production systems that use Flyway, Liquibase, or
    ///     DBA-owned migrations should set this to <see langword="false" /> and apply the canonical SQL files from
    ///     <see cref="PostgreSqlAuditSchema.SqlFiles" /> or the script from
    ///     <see cref="PostgreSqlAuditSchema.GetCreateScript(PostgreSqlAuditStoreOptions?)" />.
    /// </remarks>
    public bool EnsureSchemaCreationOnStartup { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether startup should fail when the audit tables do not match
    ///     <see cref="PostgreSqlAuditSchema.CurrentSchemaVersion" />.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, <see cref="PostgreSqlAuditSchemaInitializer" /> validates the schema during
    ///     host startup even if <see cref="EnsureSchemaCreationOnStartup" /> is <see langword="false" />. Validation
    ///     runs after ensure when both options are enabled.
    /// </remarks>
    public bool ValidateSchemaCreationOnStartup { get; init; } = true;

    /// <summary>
    ///     Validates the schema and table names before the store is used.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     A schema or table name is null, empty, or whitespace.
    /// </exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SchemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(TableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(HeadsTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(CheckpointsTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(MetadataSchemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(MetadataTableName);
    }
}
