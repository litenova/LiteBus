using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies untrusted application headers never supply ingress identity or idempotency.
/// </summary>
public sealed class TransportIngressUntrustedApplicationHeaderTests
{
    /// <summary>
    ///     Verifies two deliveries sharing one header produce two rows when their broker ids differ.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WithReusedHeaderAndDistinctBrokerIds_ShouldProduceTwoRows()
    {
        var (store, handler) = CreateHandler();

        await handler.AcceptAsync(CreateMessage("broker-1", "reused-header-id")).ConfigureAwait(false);
        await handler.AcceptAsync(CreateMessage("broker-2", "reused-header-id")).ConfigureAwait(false);

        var stored = store.GetAll();

        stored.Should().HaveCount(2);
        stored.Should().ContainSingle(envelope => envelope.IdempotencyKey == "ingress:commands.inbox:broker-1");
        stored.Should().ContainSingle(envelope => envelope.IdempotencyKey == "ingress:commands.inbox:broker-2");
    }

    /// <summary>
    ///     Verifies a redelivery carrying the same broker id resolves the existing row.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WithSameBrokerId_ShouldResolveExistingRow()
    {
        var (store, handler) = CreateHandler();

        await handler.AcceptAsync(CreateMessage("broker-1", "reused-header-id")).ConfigureAwait(false);
        await handler.AcceptAsync(CreateMessage("broker-1", "reused-header-id")).ConfigureAwait(false);

        var stored = store.GetAll();

        stored.Should().ContainSingle();
        stored[0].IdempotencyKey.Should().Be("ingress:commands.inbox:broker-1");
    }

    /// <summary>
    ///     Creates an ingress handler with default safety options and its backing store.
    /// </summary>
    /// <returns>The store and the handler writing to it.</returns>
    private static (InMemoryInboxStore Store, TransportInboxIngressHandler Handler) CreateHandler()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<HeaderProbeCommand>("orders.commands.ship");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportInboxIngressOptions());

        return (store, handler);
    }

    /// <summary>
    ///     Creates a transport delivery carrying a broker id property and a wire header id.
    /// </summary>
    /// <param name="brokerId">The broker delivery id carried on the message property.</param>
    /// <param name="headerId">The untrusted application header id.</param>
    /// <returns>A transport message for untrusted header assertions.</returns>
    private static TransportMessage CreateMessage(string brokerId, string headerId)
    {
        return new TransportMessage
        {
            Body = """{"orderId":"7"}"""u8.ToArray(),
            Destination = "commands.inbox",
            MessageId = brokerId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = headerId,
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }

    /// <summary>
    ///     Sample command accepted through transport ingress.
    /// </summary>
    private sealed record HeaderProbeCommand
    {
        /// <summary>
        ///     Gets the order identifier from the transport payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }
}
