using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs shared outbox store contract tests against the in-memory Entity Framework Core provider.
/// </summary>
public sealed class EfCoreOutboxStoreContractTests : OutboxStoreContractTests, IDisposable
{
    /// <summary>
    ///     The database name used to isolate this test class.
    /// </summary>
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    /// <summary>
    ///     The outbox store under test.
    /// </summary>
    private EfCoreOutboxStore? _store;

    /// <summary>
    ///     Verifies independent stores sharing one in-memory database cannot lease the same message.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_AcrossStoreInstances_ShouldLeaseDisjointMessages()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        using var saveBarrier = new Barrier(2);
        var synchronizeLeaseSaves = false;

        IOutboxDbContext CreateSharedContext()
        {
            var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            var context = new TestOutboxDbContext(options);
            context.SavingChanges += (_, _) =>
            {
                if (synchronizeLeaseSaves)
                {
                    saveBarrier.SignalAndWait(TimeSpan.FromMilliseconds(250));
                }
            };
            return context;
        }

        var firstStore = new EfCoreOutboxStore(
            _ => Task.FromResult(CreateSharedContext()),
            new EntityFrameworkCoreOutboxStoreOptions());
        var secondStore = new EfCoreOutboxStore(
            _ => Task.FromResult(CreateSharedContext()),
            new EntityFrameworkCoreOutboxStoreOptions());
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await firstStore.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.concurrent-lease",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        synchronizeLeaseSaves = true;
        var request = new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-a",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };

        var firstLease = firstStore.LeasePendingAsync(request);
        var secondLease = secondStore.LeasePendingAsync(request with { LeaseOwner = "publisher-b" });
        var leases = await Task.WhenAll(firstLease, secondLease).ConfigureAwait(false);

        leases.SelectMany(batch => batch).Should().ContainSingle(envelope => envelope.Id == messageId);
    }

    /// <summary>
    ///     Releases resources held by the current test class.
    /// </summary>
    public void Dispose()
    {
        _store = null;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        var context = CreateContext();
        _store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EntityFrameworkCoreOutboxStoreOptions());
        return new OutboxStoreContracts(_store, _store, _store, _store, _store, _store, _store, _store);
    }

    /// <summary>
    ///     Creates a new in-memory database context configured for outbox contract tests.
    /// </summary>
    /// <returns>The database context.</returns>
    private TestOutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Test database context that exposes outbox messages.
    /// </summary>
    private sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TestOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
