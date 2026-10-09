using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests.Dispatch;

/// <summary>
///     Verifies <see cref="TransportOutboxDispatcher" /> maps FIFO addressing fields onto the publish request.
/// </summary>
public sealed class TransportOutboxDispatcherFifoFieldsTests
{
    /// <summary>
    ///     Verifies the idempotency key becomes the deduplication key and the tenant id becomes the group id.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenIdempotencyKeyAndTenantIdAreSet_ShouldMapFifoFields()
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport);

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            TenantId = "tenant-east",
            IdempotencyKey = "idem-1"
        }).ConfigureAwait(false);

        var published = transport.Published.Single();
        published.DeduplicationKey.Should().Be("idem-1");
        published.MessageGroupId.Should().Be("tenant-east");
    }

    /// <summary>
    ///     Verifies the message id and contract name are used when the envelope carries no idempotency key or tenant.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenIdempotencyKeyAndTenantIdAreMissing_ShouldFallBackToMessageIdAndContractName()
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport);
        var messageId = Guid.NewGuid();

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        }).ConfigureAwait(false);

        var published = transport.Published.Single();
        published.DeduplicationKey.Should().Be(messageId.ToString("D"));
        published.MessageGroupId.Should().Be("orders.events.order-submitted");
    }

    /// <summary>
    ///     Verifies blank keys fall back the same way as missing keys.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WhenIdempotencyKeyAndTenantIdAreBlank_ShouldFallBackToMessageIdAndContractName()
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport);
        var messageId = Guid.NewGuid();

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            TenantId = "   ",
            IdempotencyKey = string.Empty
        }).ConfigureAwait(false);

        var published = transport.Published.Single();
        published.DeduplicationKey.Should().Be(messageId.ToString("D"));
        published.MessageGroupId.Should().Be("orders.events.order-submitted");
    }

    /// <summary>
    ///     Creates a dispatcher with a registered contract for the test envelope.
    /// </summary>
    /// <param name="transport">The fake transport recording publications.</param>
    /// <returns>A dispatcher publishing through the fake transport.</returns>
    private static TransportOutboxDispatcher CreateDispatcher(TestMessageTransport transport)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");

        return new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events"
            });
    }

    /// <summary>
    ///     Sample event used by FIFO mapping tests.
    /// </summary>
    private sealed record TestOrderSubmittedEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }
}
