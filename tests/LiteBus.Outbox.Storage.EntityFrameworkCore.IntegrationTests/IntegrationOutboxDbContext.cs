using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Integration test database context that applies outbox model configuration.
/// </summary>
internal sealed class IntegrationOutboxDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _storeOptions;

    /// <summary>
    ///     The storage provider used for model configuration.
    /// </summary>
    private readonly EfCoreStorageProvider _storageProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IntegrationOutboxDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="storageProvider">The storage provider used for model configuration.</param>
    public IntegrationOutboxDbContext(
        DbContextOptions<IntegrationOutboxDbContext> options,
        EfCoreOutboxStoreOptions storeOptions,
        EfCoreStorageProvider storageProvider = EfCoreStorageProvider.PostgreSql)
        : base(options)
    {
        _storeOptions = storeOptions;
        _storageProvider = storageProvider;
    }

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(_storeOptions, _storageProvider);
    }
}
