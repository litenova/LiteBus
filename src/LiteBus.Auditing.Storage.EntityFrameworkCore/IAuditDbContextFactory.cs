namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Opens isolated audit database contexts for records that cannot ride the business transaction.
/// </summary>
/// <remarks>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message must survive the rollback of the
///         change it describes, so the trail stages it on a fresh context outside the mediation scope and
///         saves it immediately. The application registers <see cref="EfCoreAuditDbContextFactory{TContext}" />
///         for its database context to supply those contexts; the store creates and disposes one per record.
///     </para>
/// </remarks>
public interface IAuditDbContextFactory
{
    /// <summary>
    ///     Creates a new audit database context.
    /// </summary>
    /// <returns>The created context. The caller disposes it.</returns>
    IAuditDbContext CreateDbContext();
}
