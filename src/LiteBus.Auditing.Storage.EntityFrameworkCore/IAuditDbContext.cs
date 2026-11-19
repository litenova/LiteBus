using Microsoft.EntityFrameworkCore;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Contract for an Entity Framework Core database context that exposes audit chain persistence.
/// </summary>
/// <remarks>
///     <para>
///         Application <see cref="DbContext" /> types implement this interface and register the context with
///         dependency injection. <see cref="EfCoreAuditChainStore{TDocument}" /> resolves the context from the
///         mediation scope, so staged documents commit or roll back with the business change.
///     </para>
///     <para>
///         Stored documents need no <see cref="DbSet{TEntity}" /> property on the application context. Calling
///         <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
///         <c>OnModelCreating</c> adds the default document to the model, and the generic overload adds a custom
///         document type instead.
///     </para>
/// </remarks>
public interface IAuditDbContext
{
    /// <summary>
    ///     Gets the head of every audit chain segment persisted by the application database.
    /// </summary>
    DbSet<AuditSegmentHeadEntity> AuditSegmentHeads { get; }

    /// <summary>
    ///     Gets the stored audit checkpoints persisted by the application database.
    /// </summary>
    DbSet<AuditCheckpointEntity> AuditCheckpoints { get; }
}
