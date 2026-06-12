using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Kafka;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     End-to-end outbox dispatch integration tests for the Kafka transport adapter.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaOutboxDispatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaOutboxDispatchIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured Kafka topic.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic()
    {
        var topic = KafkaTransportTestInfrastructure.CreateTopic("outbox-dispatch");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, topic);
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var provider = BuildProvider(topic);

        try
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
                    Trace = new MessageTrace.Workflow("corr-kafka-outbox", "cause-kafka-outbox"),
                    Tenant = new TenantScope.Isolated("tenant-kafka-east"),
                    Target = new PublicationTarget.Topic(topic)
                }
            });

            await processor.ProcessPendingAsync();

            store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

            var (body, headers) = await KafkaTransportTestInfrastructure.ConsumeOneAsync(
                _fixture.TransportOptions.BootstrapServers,
                topic,
                TimeSpan.FromSeconds(30));

            var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            payload!.OrderId.Should().Be(orderId);
            headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
            headers[TransportHeaders.ContractName].Should().Be("orders.order-submitted");
            headers[TransportHeaders.ContractVersion].Should().Be("1");
            headers[TransportHeaders.CorrelationId].Should().Be("corr-kafka-outbox");
            headers[TransportHeaders.CausationId].Should().Be("cause-kafka-outbox");
            headers[TransportHeaders.TenantId].Should().Be("tenant-kafka-east");
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider);
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
        var topic = KafkaTransportTestInfrastructure.CreateTopic("outbox-route");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, topic);
        var messageId = Guid.NewGuid();
        var provider = BuildProvider(topic);

        try
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
            });

            await processor.ProcessPendingAsync();

            store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

            var (_, headers) = await KafkaTransportTestInfrastructure.ConsumeOneAsync(
                _fixture.TransportOptions.BootstrapServers,
                topic,
                TimeSpan.FromSeconds(30));

            headers[TransportHeaders.ContractName].Should().Be(contractRoute);
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider);
        }
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by Kafka outbox dispatch tests.
    /// </summary>
    /// <param name="topic">The Kafka topic passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string topic)
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
                        LeaseOwner = "kafka-outbox-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseKafkaDispatch(
                        transport => transport.DefaultDestination = topic,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider();
    }
}