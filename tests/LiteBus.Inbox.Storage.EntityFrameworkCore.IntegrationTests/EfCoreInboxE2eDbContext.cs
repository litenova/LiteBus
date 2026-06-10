using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Base inbox database context for processor end-to-end tests.
/// </summary>
/// <remarks>
///     Each test class derives a unique concrete context type so EF Core model caching stays isolated per scenario.
/// </remarks>
internal abstract class EfCoreInboxE2eDbContext : DbContext, IInboxDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EfCoreInboxStoreOptions _storeOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxE2eDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    protected EfCoreInboxE2eDbContext(DbContextOptions options, EfCoreInboxStoreOptions storeOptions)
        : base(options)
    {
        _storeOptions = storeOptions;
    }

    /// <inheritdoc />
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
    }
}
