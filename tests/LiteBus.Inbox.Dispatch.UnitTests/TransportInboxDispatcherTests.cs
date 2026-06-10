using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Inbox.Dispatch.UnitTests;

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
        var transport = new FakeMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestShipCommand>("orders.commands.ship", 1);

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
        });

        transport.Published.Should().ContainSingle();
        var published = transport.Published.Single();
        published.Destination.Should().Be("orders.commands");
        published.Route.Should().Be("orders.commands.ship");
        published.MessageId.Should().Be(messageId.ToString("D"));
        published.CorrelationId.Should().Be("corr-1");
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
}
