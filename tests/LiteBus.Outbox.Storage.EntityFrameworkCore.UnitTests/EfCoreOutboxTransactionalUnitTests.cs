using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for deferred outbox writes through
///     <see cref="EfCoreOutboxStore.UseExistingDbContext{TContext}(TContext)" />.
/// </summary>
public sealed class EfCoreOutboxTransactionalUnitTests
{
    /// <summary>
    ///     Confirms <see cref="EfCoreOutboxStore.AddAsync(OutboxEnvelope, CancellationToken)" /> does not persist until the
    ///     caller saves changes.
    /// </summary>
    [Fact]
    public async Task UseExistingDbContext_defers_persistence_until_save_changes()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var context = new TestOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EntityFrameworkCoreOutboxStoreOptions());
        var transactionalStore = store.UseExistingDbContext(context);

        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"1"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };

        await transactionalStore.AddAsync(envelope);

        context.OutboxMessages.Local.Should().ContainSingle(message => message.Id == envelope.Id);
        (await context.OutboxMessages.CountAsync()).Should().Be(0);

        await context.SaveChangesAsync();

        (await context.OutboxMessages.CountAsync()).Should().Be(1);
    }

    /// <summary>
    ///     Minimal outbox context for in-memory transactional tests.
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
            modelBuilder.Entity<OutboxMessageEntity>(entity =>
            {
                entity.HasKey(message => message.Id);
            });
        }
    }
}