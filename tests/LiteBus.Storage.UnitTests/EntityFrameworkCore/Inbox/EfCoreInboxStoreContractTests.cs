using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs shared inbox store contract tests against the in-memory Entity Framework Core provider.
/// </summary>
public sealed class EfCoreInboxStoreContractTests : InboxStoreContractTests
{
    /// <summary>
    ///     The database name used to isolate each test method.
    /// </summary>
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Verifies independent stores sharing one in-memory database cannot lease the same command.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_AcrossStoreInstances_ShouldLeaseDisjointCommands()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        using var saveBarrier = new Barrier(2);
        var synchronizeLeaseSaves = false;

        IInboxDbContext CreateSharedContext()
        {
            var options = new DbContextOptionsBuilder<TestInboxDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            var context = new TestInboxDbContext(options);
            context.SavingChanges += (_, _) =>
            {
                if (synchronizeLeaseSaves)
                {
                    saveBarrier.SignalAndWait(TimeSpan.FromMilliseconds(250));
                }
            };
            return context;
        }

        var firstStore = new EfCoreInboxStore(
            _ => Task.FromResult(CreateSharedContext()),
            new EntityFrameworkCoreInboxStoreOptions());
        var secondStore = new EfCoreInboxStore(
            _ => Task.FromResult(CreateSharedContext()),
            new EntityFrameworkCoreInboxStoreOptions());
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await firstStore.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.concurrent-lease",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        synchronizeLeaseSaves = true;
        var request = new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-a",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };

        var firstLease = firstStore.LeasePendingAsync(request);
        var secondLease = secondStore.LeasePendingAsync(request with { LeaseOwner = "worker-b" });
        var leases = await Task.WhenAll(firstLease, secondLease).ConfigureAwait(false);

        leases.SelectMany(batch => batch).Should().ContainSingle(envelope => envelope.Id == messageId);
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        var store = new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(CreateContext()), new EntityFrameworkCoreInboxStoreOptions());
        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }

    /// <summary>
    ///     Creates a new in-memory database context configured for inbox contract tests.
    /// </summary>
    /// <returns>The database context.</returns>
    private TestInboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var context = new TestInboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Test database context that exposes inbox messages.
    /// </summary>
    private sealed class TestInboxDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TestInboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
