using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for <see cref="LiteBusOutboxSaveChangesInterceptor" /> pending queue behavior.
/// </summary>
public sealed class LiteBusOutboxSaveChangesInterceptorUnitTests
{
    /// <summary>
    ///     Confirms pending envelopes are cleared before the flush exception so a later save does not replay them.
    /// </summary>
    [Fact]
    public async Task SavingChanges_when_context_is_not_outbox_db_context_should_clear_pending_without_replay()
    {
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();
        var envelope = CreateFullEnvelope();

        interceptor.Enqueue(envelope);

        var options = new DbContextOptionsBuilder<NonOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        await using var context = new NonOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        var replayedEnvelope = envelope with { Id = Guid.NewGuid(), Payload = """{"replayed":false}""" };
        interceptor.Enqueue(replayedEnvelope);

        var outboxOptions = new DbContextOptionsBuilder<InterceptorOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        await using var outboxContext = new InterceptorOutboxDbContext(outboxOptions);
        await outboxContext.Database.EnsureCreatedAsync();

        await outboxContext.SaveChangesAsync();

        var stored = await outboxContext.OutboxMessages.SingleAsync();
        stored.Id.Should().Be(replayedEnvelope.Id);
        stored.Payload.Should().Contain("replayed");
        stored.IdempotencyKey.Should().Be(envelope.IdempotencyKey);
        stored.TraceContext.Should().Be(envelope.TraceContext);
    }

    private static OutboxEnvelope CreateFullEnvelope()
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 3,
            Payload = """{"orderId":"1"}""",
            Topic = "orders",
            CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
            VisibleAfter = new DateTimeOffset(2026, 6, 4, 10, 5, 0, TimeSpan.Zero),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = null,
            CorrelationId = "corr",
            CausationId = "cause",
            TenantId = "tenant",
            IdempotencyKey = "idem-full",
            TraceContext = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
        };
    }

    private sealed class NonOutboxDbContext : DbContext
    {
        public NonOutboxDbContext(DbContextOptions<NonOutboxDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class InterceptorOutboxDbContext : DbContext, IOutboxDbContext
    {
        public InterceptorOutboxDbContext(DbContextOptions<InterceptorOutboxDbContext> options)
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
