using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.AzureServiceBus;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Azure;

/// <summary>
///     End-to-end outbox dispatch integration tests for the Azure Service Bus transport adapter.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusOutboxDispatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusOutboxDispatchIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured Service Bus queue.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue()
    {
        var queueName = _fixture.ResolveQueue("outbox-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var provider = BuildProvider(queueName);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
        {
            Message = new OrderSubmittedIntegrationEvent { OrderId = orderId },
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Trace = new MessageTrace.Workflow("corr-azure-outbox", "cause-azure-outbox"),
                Tenant = new TenantScope.Isolated("tenant-azure-east"),
                Target = new PublicationTarget.Topic(queueName)
            }
        });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        var (body, headers) = await AzureServiceBusTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.TransportOptions.ConnectionString,
            queueName,
            TimeSpan.FromSeconds(45));

        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload!.OrderId.Should().Be(orderId);
        headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be("orders.order-submitted");
        headers[TransportHeaders.ContractVersion].Should().Be("1");
        headers[TransportHeaders.CorrelationId].Should().Be("corr-azure-outbox");
        headers[TransportHeaders.CausationId].Should().Be("cause-azure-outbox");
        headers[TransportHeaders.TenantId].Should().Be("tenant-azure-east");
    }

    /// <summary>
    ///     Verifies that contract-name routing is used when no topic is stored on the envelope.
    /// </summary>
    /// <returns>A task that completes when contract-name routing succeeds.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute()
    {
        const string contractRoute = "orders.order-submitted";
        var queueName = _fixture.ResolveQueue("outbox-route");
        var messageId = Guid.NewGuid();

        await using var provider = BuildProvider(queueName);
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

        var (_, headers) = await AzureServiceBusTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.TransportOptions.ConnectionString,
            queueName,
            TimeSpan.FromSeconds(45));

        headers[TransportHeaders.ContractName].Should().Be(contractRoute);
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by Azure outbox dispatch tests.
    /// </summary>
    /// <param name="queueName">The queue name passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string queueName)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "azure-outbox-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseAzureServiceBusDispatch(
                        transport => transport.DefaultDestination = queueName,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider();
    }
}