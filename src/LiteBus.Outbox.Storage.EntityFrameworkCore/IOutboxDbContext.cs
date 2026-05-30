using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Contract for an Entity Framework Core database context that exposes outbox message persistence.
/// </summary>
/// <remarks>
///     Application <see cref="DbContext" /> types implement this interface and register the context with
///     dependency injection. <see cref="EfCoreOutboxStore" /> resolves the context from a scope per store
///     operation.
/// </remarks>
public interface IOutboxDbContext
{
    /// <summary>
    ///     Gets the outbox messages persisted by the application database.
    /// </summary>
    DbSet<OutboxMessageEntity> OutboxMessages { get; }
}
