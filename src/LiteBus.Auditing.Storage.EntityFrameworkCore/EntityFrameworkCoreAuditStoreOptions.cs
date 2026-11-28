namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Defines Entity Framework Core audit store options.
/// </summary>
/// <remarks>
///     <para>
///         The application holds these options and passes them to
///         <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
///         <c>OnModelCreating</c>. The chain store never reads table names itself: it queries through
///         <see cref="IAuditDbContext" />, so the model configuration is the one place schema names live.
///     </para>
/// </remarks>
public sealed record EntityFrameworkCoreAuditStoreOptions
{
    /// <summary>
    ///     Gets the database schema name that stores audit records, segment heads, and checkpoints.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the table name that stores the default audit documents.
    /// </summary>
    /// <remarks>
    ///     A custom document type keeps the table its application maps, so this name applies only when the
    ///     model configuration maps <see cref="EfCoreAuditRecord" />.
    /// </remarks>
    public string AuditRecordsTableName { get; init; } = "litebus_audit_records";

    /// <summary>
    ///     Gets the table name that stores audit segment heads.
    /// </summary>
    public string SegmentHeadsTableName { get; init; } = "litebus_audit_segment_heads";

    /// <summary>
    ///     Gets the table name that stores audit checkpoints.
    /// </summary>
    public string CheckpointsTableName { get; init; } = "litebus_audit_checkpoints";
}
