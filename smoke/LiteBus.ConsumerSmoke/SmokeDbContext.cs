using LiteBus.Commands.Storage.Marten;
using LiteBus.ConsumerSmoke;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Marten;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     The application database context, holding both LiteBus tables, as the issue #167 report configured it.
/// </summary>
internal sealed class SmokeDbContext : DbContext, IInboxDbContext, IOutboxDbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public SmokeDbContext(DbContextOptions<SmokeDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    ///     Gets the inbox messages.
    /// </summary>
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    /// <summary>
    ///     Gets the outbox messages.
    /// </summary>
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <summary>
    ///     Gets the batch orders written by the command batch scenario.
    /// </summary>
    public DbSet<SmokeBatchOrder> SmokeBatchOrders => Set<SmokeBatchOrder>();

    /// <summary>
    ///     Creates a context on the smoke database.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A new context.</returns>
    internal static SmokeDbContext Create(string connectionString)
    {
        return new SmokeDbContext(new DbContextOptionsBuilder<SmokeDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration(
            modelBuilder,
            new EntityFrameworkCoreInboxStoreOptions(),
            EfCoreStorageProvider.PostgreSql);
        OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration(
            modelBuilder,
            new EntityFrameworkCoreOutboxStoreOptions(),
            EfCoreStorageProvider.PostgreSql);

        modelBuilder.Entity<SmokeBatchOrder>(entity =>
        {
            entity.ToTable("SmokeBatchOrders");
            entity.HasKey(order => order.OrderId);
            entity.Property(order => order.OrderId).HasColumnName("order_id");
            entity.Property(order => order.Total).HasColumnName("total").HasColumnType("numeric");
        });
    }
}

/// <summary>
///     One batch order row written by the command batch smoke handlers.
/// </summary>
internal sealed class SmokeBatchOrder
{
    /// <summary>
    ///     Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    ///     Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }
}

/// <summary>
///     Commits the scoped smoke context as the mediation unit of work.
/// </summary>
internal sealed class SmokeDbContextUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
{
    /// <summary>
    ///     The scoped context the handlers staged their rows on.
    /// </summary>
    private readonly SmokeDbContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeDbContextUnitOfWork" /> class.
    /// </summary>
    /// <param name="context">The scoped context the handlers staged their rows on.</param>
    public SmokeDbContextUnitOfWork(SmokeDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        _context.ChangeTracker.Clear();
    }
}
