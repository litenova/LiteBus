using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Integration test database context that applies audit model configuration.
/// </summary>
internal sealed class EfCoreAuditTestDbContext : DbContext, IAuditDbContext
{
    /// <summary>
    ///     The store options that control schema mapping.
    /// </summary>
    private readonly EntityFrameworkCoreAuditStoreOptions _storeOptions;

    /// <summary>
    ///     The storage provider used for model configuration.
    /// </summary>
    private readonly EfCoreStorageProvider? _storageProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditTestDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="storeOptions">The audit store options.</param>
    /// <param name="storageProvider">The storage provider used for model configuration.</param>
    public EfCoreAuditTestDbContext(
        DbContextOptions<EfCoreAuditTestDbContext> options,
        EntityFrameworkCoreAuditStoreOptions storeOptions,
        EfCoreStorageProvider? storageProvider = null)
        : base(options)
    {
        _storeOptions = storeOptions;
        _storageProvider = storageProvider;
    }

    /// <summary>
    ///     Gets the provider and table names that isolate this context model in the Entity Framework Core
    ///     model cache.
    /// </summary>
    internal (EfCoreStorageProvider? Provider, string Schema, string RecordsTable, string HeadsTable, string CheckpointsTable) ModelKey => (
        _storageProvider,
        _storeOptions.SchemaName,
        _storeOptions.AuditRecordsTableName,
        _storeOptions.SegmentHeadsTableName,
        _storeOptions.CheckpointsTableName);

    /// <inheritdoc />
    public DbSet<AuditSegmentHeadEntity> AuditSegmentHeads => Set<AuditSegmentHeadEntity>();

    /// <inheritdoc />
    public DbSet<AuditCheckpointEntity> AuditCheckpoints => Set<AuditCheckpointEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(_storeOptions, _storageProvider);
    }

    /// <summary>
    ///     Keys the Entity Framework Core model cache by provider and table names, so SQLite and PostgreSQL
    ///     test contexts map their own columns instead of reusing the model the first test built.
    /// </summary>
    internal sealed class ModelCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(DbContext context, bool designTime)
        {
            return context is EfCoreAuditTestDbContext audit
                ? (context.GetType(), audit.ModelKey, designTime)
                : (context.GetType(), designTime);
        }
    }
}
