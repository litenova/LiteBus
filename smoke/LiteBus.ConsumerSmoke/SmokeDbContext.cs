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
    private SmokeDbContext(DbContextOptions<SmokeDbContext> options)
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
    }
}
