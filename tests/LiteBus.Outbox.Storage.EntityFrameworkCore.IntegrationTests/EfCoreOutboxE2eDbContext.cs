using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Base outbox database context for processor end-to-end tests.
/// </summary>
/// <remarks>
///     Each test class derives a unique concrete context type so EF Core model caching stays isolated per scenario.
/// </remarks>
internal abstract class EfCoreOutboxE2eDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _storeOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxE2eDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    protected EfCoreOutboxE2eDbContext(DbContextOptions options, EfCoreOutboxStoreOptions storeOptions)
        : base(options)
    {
        _storeOptions = storeOptions;
    }

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
    }
}