using LiteBus.Messaging.Abstractions;

// <docs-snippet efcore-outbox-unit-of-work>
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Commits the scoped order context after a successful mediation.
/// </summary>
public sealed class OrderUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     The scoped context that stages order changes.
    /// </summary>
    private readonly OrdersDbContext _dbContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderUnitOfWork" /> class.
    /// </summary>
    /// <param name="dbContext">The scoped context that stages order changes.</param>
    public OrderUnitOfWork(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
// </docs-snippet>
