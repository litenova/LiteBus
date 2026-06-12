using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InMemory;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.InMemory;

/// <summary>
///     End-to-end outbox transport dispatch tests using the in-memory transport adapter.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryOutboxDispatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured in-memory destination.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination()
    {
        var destination = CreateDestination("outbox-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildProvider(destination);
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
        await using var consumer = await StartReceiveOneAsync(broker, destination, received);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
        {
            Message = new OrderSubmittedIntegrationEvent { OrderId = orderId },
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Trace = new MessageTrace.Workflow("corr-outbox-inmemory", "cause-outbox-inmemory"),
                Tenant = new TenantScope.Isolated("tenant-east"),
                Target = new PublicationTarget.Topic(destination)
            }
        });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transportMessage = await received.Task.WaitAsync(cancellationSource.Token);

        var json = TransportMessageAssertions.ReadBody(transportMessage);
        json.Should().Be(store.Get(messageId).Payload);

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.MessageId)
            .Should().Be(messageId.ToString("D"));

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.CorrelationId)
            .Should().Be("corr-outbox-inmemory");

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.ContractName)
            .Should().Be("orders.order-submitted");

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.ContractVersion)
            .Should().Be("1");

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.CausationId)
            .Should().Be("cause-outbox-inmemory");

        TransportMessageAssertions.GetHeader(transportMessage, TransportHeaders.TenantId)
            .Should().Be("tenant-east");
    }

    /// <summary>
    ///     Verifies that contract-name routing is used when no topic is stored on the envelope.
    /// </summary>
    /// <returns>A task that completes when contract-name routing succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute()
    {
        const string contractRoute = "orders.order-submitted";
        var destination = CreateDestination("outbox-route");
        var messageId = Guid.NewGuid();
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildProvider(destination);
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
        await using var consumer = await StartReceiveOneAsync(broker, destination, received);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
        {
            Message = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId)
            }
        });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transportMessage = await received.Task.WaitAsync(cancellationSource.Token);
        transportMessage.Route.Should().Be(contractRoute);
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by the end-to-end tests.
    /// </summary>
    /// <param name="destination">The in-memory destination passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string destination)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "outbox-inmemory-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryDispatch(transport => transport.DefaultDestination = destination);
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Creates a unique destination name for the current test run.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>A destination name safe for in-memory transport routing.</returns>
    private static string CreateDestination(string prefix)
    {
        return $"litebus-inmemory-{prefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Starts a consumer that completes the supplied task source when one message arrives.
    /// </summary>
    /// <param name="broker">The shared in-memory broker backing the consumer.</param>
    /// <param name="destination">The destination name to subscribe to.</param>
    /// <param name="received">The task source completed with the first received message.</param>
    /// <returns>The started consumer that the caller must stop and dispose.</returns>
    private static async Task<InMemoryConsumer> StartReceiveOneAsync(
        InMemoryTransportBroker broker,
        string destination,
        TaskCompletionSource<TransportMessage> received)
    {
        var consumer = new InMemoryConsumer(broker);

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = destination },
            async (message, cancellationToken) =>
            {
                received.TrySetResult(message);
                await message.AcceptAsync(cancellationToken);
            });

        return consumer;
    }
}