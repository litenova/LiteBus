using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies EF Core inbox batch recovery after a failed batch insert against file-backed SQLite.
/// </summary>
public sealed class EfCoreInboxBatchRecoveryTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxBatchRecoveryTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreInboxBatchRecoveryTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies a batch that loses a concurrent commit still inserts its genuinely new rows.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_WhenConcurrentBatchCommitsOverlap_ShouldInsertMissingRows()
    {
        var storeOptions = CreateStoreOptions();
        await using (var setup = CreateContext(storeOptions, beforeSave: null))
        {
            await setup.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        var overlappingId = Guid.NewGuid();
        var injected = false;

        async Task SeedOverlappingRowAsync(CancellationToken cancellationToken)
        {
            if (injected)
            {
                return;
            }

            injected = true;

            var seeder = CreateContext(storeOptions, beforeSave: null);
            await using var configuredSeeder = seeder.ConfigureAwait(false);
            seeder.InboxMessages.Add(new InboxMessageEntity
            {
                Id = overlappingId,
                ContractName = "tests.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                AttemptCount = 0,
                Status = InboxStatus.Pending,
                TenantId = string.Empty
            });
            await seeder.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(CreateContext(storeOptions, SeedOverlappingRowAsync)),
            storeOptions);
        var now = DateTimeOffset.UtcNow;

        var results = await store.AddBatchAsync([
            CreatePendingEnvelope(overlappingId, now),
            CreatePendingEnvelope(Guid.NewGuid(), now),
            CreatePendingEnvelope(Guid.NewGuid(), now)
        ]).ConfigureAwait(false);

        results.Should().HaveCount(3);
        results[0].Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        results[1].Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        results[2].Outcome.Should().Be(InboxAcceptOutcome.Accepted);

        await using var verification = CreateContext(storeOptions, beforeSave: null);
        (await verification.InboxMessages.CountAsync().ConfigureAwait(false)).Should().Be(3);
    }

    /// <summary>
    ///     Verifies a non-conflict batch failure surfaces the original database error instead of invented outcomes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_WhenSaveChangesFailsWithoutConflict_ShouldSurfaceOriginalError()
    {
        var storeOptions = CreateStoreOptions();
        await using (var setup = CreateContext(storeOptions, beforeSave: null))
        {
            await setup.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        static Task FailSaveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new DbUpdateException(
                "Simulated transient inbox batch failure.",
                new TimeoutException("simulated-transient-timeout"));
        }

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(CreateContext(storeOptions, FailSaveAsync)),
            storeOptions);
        var now = DateTimeOffset.UtcNow;

        var action = () => store.AddBatchAsync([
            CreatePendingEnvelope(Guid.NewGuid(), now),
            CreatePendingEnvelope(Guid.NewGuid(), now)
        ]);

        var thrown = await action.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        thrown.Which.InnerException.Should().BeOfType<DbUpdateException>();
        thrown.Which.InnerException!.InnerException.Should().BeOfType<TimeoutException>()
            .Which.Message.Should().Be("simulated-transient-timeout");
    }

    /// <summary>
    ///     Creates isolated inbox store options backed by one table in the fixture database file.
    /// </summary>
    /// <returns>The store options.</returns>
    private static EntityFrameworkCoreInboxStoreOptions CreateStoreOptions()
    {
        return new EntityFrameworkCoreInboxStoreOptions
        {
            SchemaName = "main",
            TableName = $"inbox_recovery_{Guid.NewGuid():N}",
            LeaseProvider = EfCoreStorageProvider.Sqlite
        };
    }

    /// <summary>
    ///     Creates a recovery test context connected to the fixture database file.
    /// </summary>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <param name="beforeSave">The hook that runs before every save, if any.</param>
    /// <returns>The configured test context.</returns>
    private RecoveryInboxDbContext CreateContext(
        EntityFrameworkCoreInboxStoreOptions storeOptions,
        Func<CancellationToken, Task>? beforeSave)
    {
        var options = new DbContextOptionsBuilder<RecoveryInboxDbContext>()
            .UseSqlite(_fixture.ConnectionString)
            .Options;

        return new RecoveryInboxDbContext(options, storeOptions, beforeSave);
    }

    /// <summary>
    ///     Creates a pending envelope for batch recovery tests.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="createdAt">The storage timestamp.</param>
    /// <returns>A pending envelope ready for append.</returns>
    private static InboxEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }

    /// <summary>
    ///     Test database context that runs a hook before every save.
    /// </summary>
    private sealed class RecoveryInboxDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     The hook that runs before every save, if any.
        /// </summary>
        private readonly Func<CancellationToken, Task>? _beforeSave;

        /// <summary>
        ///     The store options that control schema mapping.
        /// </summary>
        private readonly EntityFrameworkCoreInboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecoveryInboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The inbox store options.</param>
        /// <param name="beforeSave">The hook that runs before every save, if any.</param>
        public RecoveryInboxDbContext(
            DbContextOptions<RecoveryInboxDbContext> options,
            EntityFrameworkCoreInboxStoreOptions storeOptions,
            Func<CancellationToken, Task>? beforeSave)
            : base(options)
        {
            _storeOptions = storeOptions;
            _beforeSave = beforeSave;
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_beforeSave is not null)
            {
                await _beforeSave(cancellationToken).ConfigureAwait(false);
            }

            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.Sqlite);
        }
    }
}
