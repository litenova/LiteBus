using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.Dispatch;

/// <summary>
///     End-to-end tests for <see cref="TransportInboxDispatcher" /> publishing leased envelopes.
/// </summary>
public sealed class TransportInboxDispatcherTests
{
    /// <summary>
    ///     Verifies dispatch publishes the envelope body and contract headers through the fake transport.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ShouldPublishEnvelopeThroughTransport()
    {
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestShipCommand>("orders.commands.ship");

        var dispatcher = new TransportInboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportInboxDispatcherOptions
            {
                DefaultDestination = "orders.commands"
            });

        var messageId = Guid.NewGuid();

        await dispatcher.DispatchAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = """{"orderId":"42"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Processing,
            AttemptCount = 1,
            CorrelationId = "corr-1"
        }).ConfigureAwait(false);

        transport.Published.Should().ContainSingle();
        var published = transport.Published.Single();
        published.Destination.Should().Be("orders.commands");
        published.Route.Should().Be("orders.commands.ship");
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
        contractRegistry.Register<TestShipCommand>("orders.commands.ship");
        var routingStrategy = new RecordingTenantRoutingStrategy();
        var dispatcher = new TransportInboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportInboxDispatcherOptions
            {
                DefaultDestination = "orders.commands"
            },
            tenantRoutingStrategy: routingStrategy);

        await dispatcher.DispatchAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Processing,
            AttemptCount = 1,
            TenantId = "tenant-west"
        }).ConfigureAwait(false);

        routingStrategy.TenantId.Should().Be("tenant-west");
        routingStrategy.ContractName.Should().Be("orders.commands.ship");
        routingStrategy.Topic.Should().Be("orders.commands.ship");
        transport.Published.Should().ContainSingle().Which.Route.Should().Be("tenant-west.orders.commands.ship");
    }

    /// <summary>
    ///     Sample command used by transport dispatch tests.
    /// </summary>
    private sealed record TestShipCommand
    {
        /// <summary>
        ///     Gets the order identifier carried by the command payload.
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
            return $"{tenantId}.{contractName}";
        }
    }
}
