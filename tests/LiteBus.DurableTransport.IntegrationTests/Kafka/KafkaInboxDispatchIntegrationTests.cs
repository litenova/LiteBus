using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     End-to-end inbox dispatch integration tests for the Kafka transport adapter.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
public sealed class KafkaInboxDispatchIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaInboxDispatchIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes payload and headers to Kafka.
    /// </summary>
    /// <returns>A task that completes when the publish assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic()
    {
        var topic = KafkaTransportTestInfrastructure.CreateTopic("inbox-dispatch");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, topic);
        var provider = BuildProvider(topic);

        try
        {
            var inbox = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();

            var workItemId = Guid.NewGuid();

            var receipt = await inbox.AcceptAsync(new InboxAcceptItem<RemoteWorkCommand>
            {
                Message = new RemoteWorkCommand
                {
                    WorkItemId = workItemId,
                    IdempotencyKey = $"work:{workItemId}"
                },
                Metadata = InboxAcceptMetadata.Immediate with
                {
                    Trace = new MessageTrace.Workflow("corr-kafka-dispatch", "cause-kafka-dispatch"),
                    Tenant = new TenantScope.Isolated("tenant-kafka")
                }
            });

            await processor.ProcessPendingAsync();

            var (body, headers) = await KafkaTransportTestInfrastructure.ConsumeOneAsync(
                _fixture.TransportOptions.BootstrapServers,
                topic,
                TimeSpan.FromSeconds(30));

            body.Should().Contain(workItemId.ToString());
            headers[TransportHeaders.MessageId].Should().Be(receipt.Id.ToString("D"));
            headers[TransportHeaders.ContractName].Should().Be(ContractName);
            headers[TransportHeaders.ContractVersion].Should().Be(ContractVersion.ToString());
            headers[TransportHeaders.CorrelationId].Should().Be("corr-kafka-dispatch");
            headers[TransportHeaders.CausationId].Should().Be("cause-kafka-dispatch");
            headers[TransportHeaders.TenantId].Should().Be("tenant-kafka");
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Kafka inbox dispatch tests.
    /// </summary>
    /// <param name="topic">The Kafka topic used for dispatch.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string topic)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "kafka-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();

                    inbox.UseKafkaDispatch(
                        transport => transport.DefaultDestination = topic,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }
}