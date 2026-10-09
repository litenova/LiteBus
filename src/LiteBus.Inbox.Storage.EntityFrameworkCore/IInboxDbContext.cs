using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Contract for an Entity Framework Core database context that exposes inbox message persistence.
/// </summary>
/// <remarks>
///     Application <see cref="DbContext" /> types implement this interface and register the context with
///     dependency injection. <see cref="EfCoreInboxStore" /> resolves the context from a scope per store
///     operation.
/// </remarks>
public interface IInboxDbContext
{
    /// <summary>
    ///     Gets the inbox messages persisted by the application database.
    /// </summary>
    DbSet<InboxMessageEntity> InboxMessages { get; }
}
