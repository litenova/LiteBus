using LiteBus.Inbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for deferred inbox writes through
///     <see cref="EfCoreInboxStore.UseExistingDbContext{TContext}(TContext)" />.
/// </summary>
public sealed class EfCoreInboxTransactionalUnitTests
{
    /// <summary>
    ///     Confirms <see cref="EfCoreInboxStore.AddAsync(InboxEnvelope, CancellationToken)" /> does not persist until the
    ///     caller saves changes.
    /// </summary>
    [Fact]
    public async Task UseExistingDbContext_defers_persistence_until_save_changes()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

         var context = new TestInboxDbContext(options);
         await using (context.ConfigureAwait(false))
         {
        await context.Database.EnsureCreatedAsync().ConfigureAwait(true);

        var store = new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(context), new EntityFrameworkCoreInboxStoreOptions());
        var transactionalStore = store.UseExistingDbContext(context);

        var envelope = new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.submit",
            ContractVersion = 1,
            Payload = """{"orderId":"1"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        };

        await transactionalStore.AddAsync(envelope).ConfigureAwait(true);

        context.InboxMessages.Local.Should().ContainSingle(message => message.Id == envelope.Id);
        (await context.InboxMessages.CountAsync().ConfigureAwait(true)).Should().Be(0);

        await context.SaveChangesAsync().ConfigureAwait(true);

        (await context.InboxMessages.CountAsync().ConfigureAwait(true)).Should().Be(1);
        }
    }

    /// <summary>
    ///     Minimal inbox context for in-memory transactional tests.
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
            modelBuilder.Entity<InboxMessageEntity>(entity =>
            {
                entity.HasKey(message => message.Id);
            });
        }
    }
}
