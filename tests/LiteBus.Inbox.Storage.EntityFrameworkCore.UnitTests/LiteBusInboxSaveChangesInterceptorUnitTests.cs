using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for <see cref="LiteBusInboxSaveChangesInterceptor" /> pending queue behavior.
/// </summary>
public sealed class LiteBusInboxSaveChangesInterceptorUnitTests
{
    /// <summary>
    ///     Confirms pending envelopes are cleared before the flush exception so a later save does not replay them.
    /// </summary>
    [Fact]
    public async Task SavingChanges_when_context_is_not_inbox_db_context_should_clear_pending_without_replay()
    {
        var interceptor = new LiteBusInboxSaveChangesInterceptor();
        var envelope = CreateFullEnvelope();

        var options = new DbContextOptionsBuilder<NonInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        await using var nonInboxContext = new NonInboxDbContext(options);
        await nonInboxContext.Database.EnsureCreatedAsync();

        interceptor.Enqueue(nonInboxContext, envelope);

        var act = () => nonInboxContext.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        var replayedEnvelope = envelope with { Id = Guid.NewGuid(), Payload = """{"replayed":false}""" };
        var inboxOptions = new DbContextOptionsBuilder<InterceptorInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        await using var inboxContext = new InterceptorInboxDbContext(inboxOptions);
        await inboxContext.Database.EnsureCreatedAsync();

        interceptor.Enqueue(inboxContext, replayedEnvelope);

        await inboxContext.SaveChangesAsync();

        var stored = await inboxContext.InboxMessages.SingleAsync();
        stored.Id.Should().Be(replayedEnvelope.Id);
        stored.Payload.Should().Contain("replayed");
        stored.IdempotencyKey.Should().Be(envelope.IdempotencyKey);
        stored.TraceContext.Should().Be(envelope.TraceContext);
    }

    /// <summary>
    ///     Confirms parallel saves on different contexts sharing one interceptor isolate pending envelopes per context.
    /// </summary>
    [Fact]
    public async Task SavingChangesAsync_when_multiple_contexts_save_concurrently_should_persist_isolated_envelopes()
    {
        const int contextCount = 8;
        var interceptor = new LiteBusInboxSaveChangesInterceptor();
        var envelopes = Enumerable.Range(0, contextCount)
            .Select(index => CreateFullEnvelope() with
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"parallel-{index}",
                Payload = $$"""{"index":{{index}}}"""
            })
            .ToArray();

        var storedIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        await Task.WhenAll(Enumerable.Range(0, contextCount).Select(async index =>
        {
            var options = new DbContextOptionsBuilder<InterceptorInboxDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddLiteBusInboxInterceptor(interceptor)
                .Options;

            await using var context = new InterceptorInboxDbContext(options);
            await context.Database.EnsureCreatedAsync();
            interceptor.Enqueue(context, envelopes[index]);
            await context.SaveChangesAsync();
            storedIds.Add((await context.InboxMessages.SingleAsync()).Id);
        }));

        storedIds.Should().BeEquivalentTo(envelopes.Select(envelope => envelope.Id));
    }

    /// <summary>
    ///     Confirms parallel enqueues on one context accumulate in the interceptor pending list for a single save.
    /// </summary>
    [Fact]
    public async Task SavingChangesAsync_when_same_context_accumulates_parallel_enqueues_should_persist_all_envelopes()
    {
        const int envelopeCount = 8;
        var interceptor = new LiteBusInboxSaveChangesInterceptor();
        var envelopes = Enumerable.Range(0, envelopeCount)
            .Select(index => CreateFullEnvelope() with
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"same-context-{index}",
                Payload = $$"""{"index":{{index}}}"""
            })
            .ToArray();

        var options = new DbContextOptionsBuilder<InterceptorInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        await using var context = new InterceptorInboxDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var enqueueLock = new object();
        await Task.WhenAll(envelopes.Select(envelope => Task.Run(() =>
        {
            lock (enqueueLock)
            {
                interceptor.Enqueue(context, envelope);
            }
        })));

        await context.SaveChangesAsync();

        var stored = await context.InboxMessages.AsNoTracking().ToListAsync();
        stored.Should().HaveCount(envelopeCount);
        stored.Select(message => message.Id).Should().OnlyHaveUniqueItems();
    }

    private static InboxEnvelope CreateFullEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.submit",
            ContractVersion = 3,
            Payload = """{"orderId":"1"}""",
            CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
            VisibleAfter = new DateTimeOffset(2026, 6, 4, 10, 5, 0, TimeSpan.Zero),
            Status = InboxStatus.Pending,
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

    private sealed class NonInboxDbContext : DbContext
    {
        public NonInboxDbContext(DbContextOptions<NonInboxDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class InterceptorInboxDbContext : DbContext, IInboxDbContext
    {
        public InterceptorInboxDbContext(DbContextOptions<InterceptorInboxDbContext> options)
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
