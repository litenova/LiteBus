using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Aws;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     End-to-end outbox dispatch integration tests for the AWS SQS transport adapter.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsOutboxDispatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsOutboxDispatchIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured SQS queue.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue()
    {
        var queueUrl = await _fixture.CreateQueueAsync("outbox-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var provider = BuildProvider(queueUrl);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
        {
            Event = new OrderSubmittedIntegrationEvent { OrderId = orderId },
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Trace = new MessageTrace.Workflow("corr-sqs-outbox", "cause-sqs-outbox"),
                Tenant = new TenantScope.Isolated("tenant-sqs-east"),
                Target = new PublicationTarget.Topic(queueUrl)
            }
        });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        var (body, headers) = await SqsTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.SqsClient,
            queueUrl,
            TimeSpan.FromSeconds(30));

        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload!.OrderId.Should().Be(orderId);
        headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be("orders.order-submitted");
        headers[TransportHeaders.ContractVersion].Should().Be("1");
        headers[TransportHeaders.CorrelationId].Should().Be("corr-sqs-outbox");
        headers[TransportHeaders.CausationId].Should().Be("cause-sqs-outbox");
        headers[TransportHeaders.TenantId].Should().Be("tenant-sqs-east");
    }

    /// <summary>
    ///     Verifies that contract-name routing is used when no topic is stored on the envelope.
    /// </summary>
    /// <returns>A task that completes when contract-name routing succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute()
    {
        const string contractRoute = "orders.order-submitted";
        var queueUrl = await _fixture.CreateQueueAsync("outbox-route");
        var messageId = Guid.NewGuid();

        await using var provider = BuildProvider(queueUrl);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
        {
            Event = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId)
            }
        });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        var (_, headers) = await SqsTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.SqsClient,
            queueUrl,
            TimeSpan.FromSeconds(30));

        headers[TransportHeaders.ContractName].Should().Be(contractRoute);
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by SQS outbox dispatch tests.
    /// </summary>
    /// <param name="queueUrl">The SQS queue URL passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string queueUrl)
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
                        LeaseOwner = "sqs-outbox-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseAwsSqsDispatch(
                        transport => transport.DefaultDestination = queueUrl,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider();
    }
}