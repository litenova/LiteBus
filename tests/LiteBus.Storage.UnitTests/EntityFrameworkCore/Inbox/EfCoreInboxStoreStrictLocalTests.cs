using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies single-add strict idempotency against rows staged on the same context.
/// </summary>
public sealed class EfCoreInboxStoreStrictLocalTests
{
    /// <summary>
    ///     Confirms a strict accept that differs from a staged row throws instead of reporting already accepted.
    /// </summary>
    [Fact]
    public async Task AddAsync_WithStrictStagedDuplicateChangedPayload_ShouldThrowConflict()
    {
        var options = new DbContextOptionsBuilder<StrictLocalInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new StrictLocalInboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var store = new EfCoreInboxStore(
                _ => Task.FromResult<IInboxDbContext>(context),
                new EntityFrameworkCoreInboxStoreOptions());
            var bound = store.UseExistingDbContext(context);

            var messageId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

            await bound.AddAsync(new InboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.place",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = InboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            }).ConfigureAwait(false);

            var action = () => bound.AddAsync(new InboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.place",
                ContractVersion = 1,
                Payload = """{"orderId":"2"}""",
                CreatedAt = now,
                Status = InboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            });

            await action.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
        }
    }

    private sealed class StrictLocalInboxDbContext : DbContext, IInboxDbContext
    {
        public StrictLocalInboxDbContext(DbContextOptions<StrictLocalInboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
