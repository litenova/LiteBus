using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.UnitTests.Dispatch;

/// <summary>
///     End-to-end tests for <see cref="TransportOutboxDispatcher" /> publishing leased envelopes.
/// </summary>
public sealed class TransportOutboxDispatcherTests
{
    /// <summary>
    ///     Verifies dispatch publishes the envelope body and contract headers through the fake transport.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ShouldPublishEnvelopeThroughTransport()
    {
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");

        var dispatcher = new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events"
            });

        var messageId = Guid.NewGuid();

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"42"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            CorrelationId = "corr-1"
        }).ConfigureAwait(false);

        transport.Published.Should().ContainSingle();
        var published = transport.Published.Single();
        published.Destination.Should().Be("orders.events");
        published.Route.Should().Be("orders.events.order-submitted");
        published.MessageId.Should().Be(messageId.ToString("D"));
        published.CorrelationId.Should().Be("corr-1");
    }

    /// <summary>
    ///     Verifies a tenant routing strategy receives envelope metadata and controls the published route.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WithTenantRoutingStrategy_ShouldUseResolvedRoute()
    {
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");
        var routingStrategy = new RecordingTenantRoutingStrategy();
        var dispatcher = new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events"
            },
            tenantRoutingStrategy: routingStrategy);

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
            Topic = "orders.priority"
        }).ConfigureAwait(false);

        routingStrategy.TenantId.Should().Be("tenant-east");
        routingStrategy.ContractName.Should().Be("orders.events.order-submitted");
        routingStrategy.Topic.Should().Be("orders.priority");
        transport.Published.Should().ContainSingle().Which.Route.Should().Be("tenant-east.orders.priority");
    }

    /// <summary>
    ///     Verifies invalid payloads skip deserialization when validation is disabled.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_validate_payload_disabled_should_publish_without_deserializing()
    {
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");

        var dispatcher = new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events",
                ValidatePayloadBeforeDispatch = false
            });

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "not-json",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        }).ConfigureAwait(false);

        transport.Published.Should().ContainSingle();
    }

    /// <summary>
    ///     Verifies invalid payloads fail fast when validation is enabled.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_validate_payload_enabled_should_throw_before_publish()
    {
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");

        var dispatcher = new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events",
                ValidatePayloadBeforeDispatch = true
            });

        var act = async () => await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "not-json",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        }).ConfigureAwait(false);

        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);
        transport.Published.Should().BeEmpty();
    }

    /// <summary>
    ///     Sample event used by transport dispatch tests.
    /// </summary>
    private sealed record TestOrderSubmittedEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }

    /// <summary>
    ///     Records tenant routing inputs and returns a deterministic route.
    /// </summary>
    private sealed class RecordingTenantRoutingStrategy : ITenantRoutingStrategy
    {
        /// <summary>
        ///     Gets the tenant identifier passed by the dispatcher.
        /// </summary>
        public string? TenantId { get; private set; }

        /// <summary>
        ///     Gets the stable contract name passed by the dispatcher.
        /// </summary>
        public string? ContractName { get; private set; }

        /// <summary>
        ///     Gets the topic hint passed by the dispatcher.
        /// </summary>
        public string? Topic { get; private set; }

        /// <inheritdoc />
        public string ResolveRoute(string? tenantId, string contractName, string? topic)
        {
            TenantId = tenantId;
            ContractName = contractName;
            Topic = topic;
            return $"{tenantId}.{topic}";
        }
    }
}
