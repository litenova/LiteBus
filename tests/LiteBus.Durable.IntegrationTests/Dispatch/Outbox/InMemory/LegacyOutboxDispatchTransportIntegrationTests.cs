using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InMemory;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.InMemory;

/// <summary>
///     End-to-end outbox transport dispatch tests using the in-memory transport adapter.
/// </summary>
public sealed class OutboxDispatchTransportIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured in-memory destination.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination()
    {
        var destination = InMemoryTransportTestInfrastructure.CreateDestination("outbox-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

         var provider = BuildProvider(destination);
         await using (provider.ConfigureAwait(false))
         {
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();

         var consumer = await InMemoryTransportTestInfrastructure.StartReceiveOneAsync(             broker,             destination,             received).ConfigureAwait(true);
         await using (consumer.ConfigureAwait(true))
         {

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
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
        store.Get(messageId).AttemptCount.Should().Be(1);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transportMessage = await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);

        var storedPayload = store.Get(messageId).Payload;
        var json = InMemoryTransportTestInfrastructure.ReadBody(transportMessage);
        json.Should().Be(storedPayload);

        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload!.OrderId.Should().Be(orderId);

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.MessageId)
            .Should().Be(messageId.ToString("D"));

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.CorrelationId)
            .Should().Be("corr-outbox-inmemory");

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.ContractName)
            .Should().Be("orders.order-submitted");

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.ContractVersion)
            .Should().Be("1");

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.CausationId)
            .Should().Be("cause-outbox-inmemory");

        InMemoryTransportTestInfrastructure.GetHeader(transportMessage, TransportHeaders.TenantId)
            .Should().Be("tenant-east");
        }
        }
    }

    /// <summary>
    ///     Verifies that contract-name routing is used when no topic is stored on the envelope.
    /// </summary>
    /// <returns>A task that completes when contract-name routing succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute()
    {
        const string contractRoute = "orders.order-submitted";
        var destination = InMemoryTransportTestInfrastructure.CreateDestination("outbox-route");
        var messageId = Guid.NewGuid();
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

         var provider = BuildProvider(destination);
         await using (provider.ConfigureAwait(false))
         {
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();

         var consumer = await InMemoryTransportTestInfrastructure.StartReceiveOneAsync(             broker,             destination,             received).ConfigureAwait(true);
         await using (consumer.ConfigureAwait(true))
         {

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
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transportMessage = await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
        transportMessage.Route.Should().Be(contractRoute);
        }
        }
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
}
