using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies idempotent enqueue resolution when both message id and idempotency key match different rows.
/// </summary>
public sealed class EfCoreOutboxFindExistingTests
{
    /// <summary>
    ///     Confirms a duplicate enqueue by idempotency key returns the row keyed by message id when both exist.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_when_id_and_idempotency_key_match_different_rows_should_prefer_message_id_row()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        var options = new DbContextOptionsBuilder<FindExistingOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

         var context = new FindExistingOutboxDbContext(options);
         await using (context.ConfigureAwait(false))
         {
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        var idRowId = Guid.NewGuid();
        var keyRowId = Guid.NewGuid();
        const string idempotencyKey = "order-99";
        var createdAt = DateTimeOffset.UtcNow;

        context.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = idRowId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"by":"id"}""",
            CreatedAt = createdAt,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        context.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = keyRowId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"by":"key"}""",
            CreatedAt = createdAt.AddSeconds(1),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        await context.SaveChangesAsync().ConfigureAwait(false);

        var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EntityFrameworkCoreOutboxStoreOptions());

        var resolved = await store.EnqueueAsync(new OutboxEnvelope
        {
            Id = idRowId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"attempt":"duplicate"}""",
            CreatedAt = createdAt.AddMinutes(1),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        }).ConfigureAwait(false);

        resolved.Id.Should().Be(idRowId);
        resolved.Payload.Should().Contain("by\":\"id");
        }
    }

    /// <summary>
    ///     Minimal outbox context for find-existing tests.
    /// </summary>
    private sealed class FindExistingOutboxDbContext : DbContext, IOutboxDbContext
    {
        public FindExistingOutboxDbContext(DbContextOptions<FindExistingOutboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessageEntity>(entity =>
            {
                entity.HasKey(message => message.Id);
                entity.Property(message => message.IdempotencyKey);
            });
        }
    }
}
