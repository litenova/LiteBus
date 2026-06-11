using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Dedicated outbox context for interceptor tests so EF model caching does not reuse contract-test mappings.
/// </summary>
internal sealed class InterceptorOutboxDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _storeOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InterceptorOutboxDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    public InterceptorOutboxDbContext(
        DbContextOptions<InterceptorOutboxDbContext> options,
        EfCoreOutboxStoreOptions storeOptions)
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