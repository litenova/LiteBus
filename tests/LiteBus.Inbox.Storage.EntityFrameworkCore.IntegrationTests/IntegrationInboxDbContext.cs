using LiteBus.Inbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Integration test database context that applies inbox model configuration.
/// </summary>
internal sealed class IntegrationInboxDbContext : DbContext, IInboxDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EfCoreInboxStoreOptions _storeOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IntegrationInboxDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    public IntegrationInboxDbContext(DbContextOptions<IntegrationInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
        : base(options)
    {
        _storeOptions = storeOptions;
    }

    /// <inheritdoc />
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(_storeOptions);
    }
}
