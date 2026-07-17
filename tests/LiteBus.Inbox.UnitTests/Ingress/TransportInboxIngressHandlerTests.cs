using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Integration-style tests for <see cref="TransportInboxIngressHandler" />.
/// </summary>
public sealed class TransportInboxIngressHandlerTests
{
    private static readonly TransportInboxIngressOptions PermissiveOptions = new()
    {
        RequireStableIdentity = false
    };

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
            new SystemTextJsonMessageSerializer(),
            PermissiveOptions);

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

        await handler.AcceptAsync(transportMessage).ConfigureAwait(false);

        var stored = store.Get(messageId);
        stored.ContractName.Should().Be("orders.commands.ship");
        stored.CorrelationId.Should().Be("corr-ingress");
        stored.Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies broker-scoped idempotency deduplicates redelivered transport messages.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WhenBrokerRedelivers_ShouldNotCreateDuplicateRows()
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
            new SystemTextJsonMessageSerializer(),
            new TransportInboxIngressOptions
            {
                RequireStableIdentity = true
            });

        const string brokerMessageId = "broker-msg-1001";

        var transportMessage = new TransportMessage
        {
            Body = """{"orderId":"99"}"""u8.ToArray(),
            Destination = "commands.inbox",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = brokerMessageId,
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };

        await handler.AcceptAsync(transportMessage).ConfigureAwait(false);
        await handler.AcceptAsync(transportMessage).ConfigureAwait(false);

        store.GetAll().Should().ContainSingle();
        store.GetAll()[0].IdempotencyKey.Should().Be("ingress:commands.inbox:broker-msg-1001");
    }

    /// <summary>
    ///     Verifies oversized delivery bodies are rejected before deserialization.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WhenBodyExceedsMaxMessageBytes_ShouldThrow()
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
            new SystemTextJsonMessageSerializer(),
            new TransportInboxIngressOptions
            {
                MaxMessageBytes = 4,
                RequireStableIdentity = false
            });

        var transportMessage = new TransportMessage
        {
            Body = """{"orderId":"too-large"}"""u8.ToArray(),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };

        var act = () => handler.AcceptAsync(transportMessage);

        await act.Should().ThrowAsync<InboxIngressException>()
            .WithMessage("*MaxMessageBytes*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies the host authorization callback receives the original delivery and cancellation token before accept.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WithAuthorizationCallback_ShouldAuthorizeBeforeStoreWrite()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestIngressCommand>("orders.commands.ship");
        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);
        TransportMessage? authorizedMessage = null;
        CancellationToken authorizedToken = default;
        var callbackCount = 0;
        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportInboxIngressOptions
            {
                RequireStableIdentity = false,
                AuthorizeDeliveryAsync = (message, cancellationToken) =>
                {
                    callbackCount++;
                    authorizedMessage = message;
                    authorizedToken = cancellationToken;
                    return Task.CompletedTask;
                }
            });
        var messageId = Guid.NewGuid();
        var transportMessage = new TransportMessage
        {
            Body = """{"orderId":"authorized"}"""u8.ToArray(),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = messageId.ToString("D"),
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            },
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
        using var cancellationSource = new CancellationTokenSource();

        await handler.AcceptAsync(transportMessage, cancellationSource.Token).ConfigureAwait(false);

        callbackCount.Should().Be(1);
        authorizedMessage.Should().BeSameAs(transportMessage);
        authorizedToken.Should().Be(cancellationSource.Token);
        store.Get(messageId).Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies batch ingress accepts each delivery independently.
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
            new SystemTextJsonMessageSerializer(),
            PermissiveOptions);

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

        await handler.AcceptBatchAsync([transportMessage]).ConfigureAwait(false);

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
