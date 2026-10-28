using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies Entity Framework Core outbox batch recovery reinserts missing rows and surfaces failures.
/// </summary>
public sealed class EfCoreOutboxBatchRecoveryTests
{
    /// <summary>
    ///     Confirms a batch that hits a unique violation still inserts rows that were never stored.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_WhenUniqueViolationLeavesNewRowsMissing_ShouldInsertMissingRows()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var context = CreateContext(databaseName);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var existingId = Guid.NewGuid();
            var newId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

            context.OutboxMessages.Add(new OutboxMessageEntity
            {
                Id = existingId,
                ContractName = "tests.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                TenantId = string.Empty
            });

            await context.SaveChangesAsync().ConfigureAwait(false);

            context.SaveFailures.Enqueue(new DbUpdateException("duplicate key value violates unique constraint \"ix_outbox\""));

            var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EntityFrameworkCoreOutboxStoreOptions());

            var results = await store.AddBatchAsync([
                new OutboxEnvelope
                {
                    Id = existingId,
                    ContractName = "tests.events.submitted",
                    ContractVersion = 1,
                    Payload = """{"orderId":"1"}""",
                    CreatedAt = now,
                    Status = OutboxStatus.Pending,
                    AttemptCount = 0,
                    TenantId = string.Empty
                },
                new OutboxEnvelope
                {
                    Id = newId,
                    ContractName = "tests.events.submitted",
                    ContractVersion = 1,
                    Payload = """{"orderId":"2"}""",
                    CreatedAt = now.AddSeconds(1),
                    Status = OutboxStatus.Pending,
                    AttemptCount = 0,
                    TenantId = string.Empty
                }
            ]).ConfigureAwait(false);

            results.Should().HaveCount(2);
            results[0].Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            results[1].Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
            results[1].Envelope.Id.Should().Be(newId);

            (await context.OutboxMessages.CountAsync().ConfigureAwait(false)).Should().Be(2);
        }
    }

    /// <summary>
    ///     Confirms a transient batch failure surfaces the original error instead of masking it.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_WhenTransientFailurePersists_ShouldChainOriginalException()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var context = CreateContext(databaseName);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            context.SaveFailures.Enqueue(new DbUpdateException("deadlock detected"));
            context.SaveFailures.Enqueue(new DbUpdateException("deadlock detected"));

            var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EntityFrameworkCoreOutboxStoreOptions());
            var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

            var action = () => store.AddBatchAsync([
                new OutboxEnvelope
                {
                    Id = Guid.NewGuid(),
                    ContractName = "tests.events.submitted",
                    ContractVersion = 1,
                    Payload = """{"orderId":"1"}""",
                    CreatedAt = now,
                    Status = OutboxStatus.Pending,
                    AttemptCount = 0,
                    TenantId = string.Empty
                }
            ]);

            var thrown = await action.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.Which.InnerException.Should().BeOfType<DbUpdateException>();
            thrown.Which.InnerException!.Message.Should().Contain("deadlock");
        }
    }

    /// <summary>
    ///     Creates a configured outbox database context with injectable save failures.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <returns>The database context.</returns>
    private static RecoveryOutboxDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<RecoveryOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new RecoveryOutboxDbContext(options);
    }

    /// <summary>
    ///     Test database context that fails saves on demand for recovery tests.
    /// </summary>
    private sealed class RecoveryOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RecoveryOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public RecoveryOutboxDbContext(DbContextOptions<RecoveryOutboxDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        ///     Gets the queued save failures consumed in order by save operations.
        /// </summary>
        public Queue<Exception?> SaveFailures { get; } = new();

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SaveFailures.Count > 0)
            {
                var failure = SaveFailures.Dequeue();

                if (failure is not null)
                {
                    throw failure;
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
