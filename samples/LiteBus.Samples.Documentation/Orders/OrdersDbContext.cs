using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

// <docs-snippet efcore-outbox-db-context>
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Stores order state with the outbox rows committed in the same transaction.
/// </summary>
public sealed class OrdersDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OrdersDbContext" /> class.
    /// </summary>
    /// <param name="options">The configured context options.</param>
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(
            new EntityFrameworkCoreOutboxStoreOptions(),
            EfCoreStorageProvider.PostgreSql);
    }
}
// </docs-snippet>
