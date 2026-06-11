using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress.UnitTests;

/// <summary>
///     Integration-style tests for <see cref="TransportInboxIngressHandler" />.
/// </summary>
public sealed class TransportInboxIngressHandlerTests
{
    /// <summary>
    ///     Verifies transport deliveries are accepted into the inbox store with mapped metadata.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_ShouldWriteEnvelopeToInboxStore()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestIngressCommand>("orders.commands.ship");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer());

        var messageId = Guid.NewGuid();

        var transportMessage = new TransportMessage
        {
            Body = """{"orderId":"99"}"""u8.ToArray(),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = messageId.ToString("D"),
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.CorrelationId] = "corr-ingress"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };

        await handler.AcceptAsync(transportMessage);

        var stored = store.Get(messageId);
        stored.ContractName.Should().Be("orders.commands.ship");
        stored.CorrelationId.Should().Be("corr-ingress");
        stored.Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies batch ingress flushes a single delivery through <see cref="IInbox.AcceptBatchAsync" />.
    /// </summary>
    [Fact]
    public async Task AcceptBatchAsync_WithSingleMessage_ShouldWriteEnvelopeToInboxStore()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestIngressCommand>("orders.commands.ship");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer());

        var messageId = Guid.NewGuid();

        var transportMessage = new TransportMessage
        {
            Body = """{"orderId":"42"}"""u8.ToArray(),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = messageId.ToString("D"),
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };

        await handler.AcceptBatchAsync([transportMessage]);

        var stored = store.Get(messageId);
        stored.ContractName.Should().Be("orders.commands.ship");
        stored.Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Sample command accepted through transport ingress.
    /// </summary>
    private sealed record TestIngressCommand
    {
        /// <summary>
        ///     Gets the order identifier from the transport payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }
}