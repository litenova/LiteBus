using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies <see cref="OutboxEnvelopeFactory" /> produces the same envelope shape as <see cref="Outbox" />.
/// </summary>
public sealed class OutboxEnvelopeFactoryTests
{
    /// <summary>
    ///     Confirms factory output matches the fields written by the auto-commit outbox writer.
    /// </summary>
    [Fact]
    public async Task CreateAsync_should_match_outbox_writer_fields()
    {
        var now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryOutboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<TestEvent>("orders.events.submitted", 2);
        var serializer = new SystemTextJsonMessageSerializer();
        var clock = new ManualTimeProvider(now);
        var factory = new OutboxEnvelopeFactory(registry, serializer, clock);
        var outbox = OutboxWriterTestFactory.Create(store, registry, serializer, clock);

        var messageId = Guid.NewGuid();
        var options = new OutboxOptions
        {
            Id = messageId,
            Topic = "orders",
            IdempotencyKey = "idem-1",
            CorrelationId = "corr-1",
            CausationId = "cause-1",
            TenantId = "tenant-1",
            TraceContext = "trace-1"
        };

        var envelope = await factory.CreateAsync(new TestEvent { OrderId = Guid.NewGuid() }, options);
        var receipt = await outbox.EnqueueAsync(new TestEvent { OrderId = Guid.NewGuid() }, options);

        envelope.Id.Should().Be(messageId);
        envelope.ContractName.Should().Be(receipt.ContractName);
        envelope.ContractVersion.Should().Be(receipt.ContractVersion);
        envelope.CreatedAt.Should().Be(receipt.StoredAt);
        envelope.Topic.Should().Be("orders");
        envelope.IdempotencyKey.Should().Be("idem-1");
        envelope.CorrelationId.Should().Be("corr-1");
        envelope.CausationId.Should().Be("cause-1");
        envelope.TenantId.Should().Be("tenant-1");
        envelope.TraceContext.Should().Be("trace-1");
        envelope.Status.Should().Be(OutboxStatus.Pending);
        envelope.AttemptCount.Should().Be(0);
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    private sealed record TestEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }
}
