using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies single-enqueue strict idempotency against rows staged on the same context.
/// </summary>
public sealed class EfCoreOutboxStoreStrictLocalTests
{
    /// <summary>
    ///     Confirms a strict enqueue that differs from a staged row throws instead of reporting already enqueued.
    /// </summary>
    [Fact]
    public async Task AddAsync_WithStrictStagedDuplicateChangedPayload_ShouldThrowConflict()
    {
        var options = new DbContextOptionsBuilder<StrictLocalOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new StrictLocalOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var store = new EfCoreOutboxStore(
                _ => Task.FromResult<IOutboxDbContext>(context),
                new EntityFrameworkCoreOutboxStoreOptions());
            var bound = store.UseExistingDbContext(context);

            var messageId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

            await bound.AddAsync(new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            }).ConfigureAwait(false);

            var action = () => bound.AddAsync(new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"2"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            });

            await action.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
        }
    }

    private sealed class StrictLocalOutboxDbContext : DbContext, IOutboxDbContext
    {
        public StrictLocalOutboxDbContext(DbContextOptions<StrictLocalOutboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
