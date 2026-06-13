using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Transport.IntegrationTesting.Kafka;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     Verifies duplicate Kafka ingress deliveries with the same message identifier are idempotent.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaIngressIdempotencyIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaIngressIdempotencyIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaIngressIdempotencyIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Kafka");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies duplicate ingress deliveries with the same message identifier create one inbox row.
    /// </summary>
    /// <returns>A task that completes when the idempotency assertion succeeds.</returns>
    [Fact]
    public async Task DuplicateMessageId_ShouldCreateSingleInboxRow()
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress-idem");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, ingressTopic).ConfigureAwait(false);

        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() });
        var headers = TransportTestHeaders.Create(messageId, ContractName, 1);

        var provider = BuildProvider(ingressTopic);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await KafkaIngressTestSupport.StartIngressAsync(provider).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            provider.GetRequiredService<InMemoryInboxStore>().Get(messageId).Status.Should().Be(InboxStatus.Pending);
        }
        finally
        {
            await KafkaIngressTestSupport.StopIngressAsync(provider).ConfigureAwait(false);
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for Kafka ingress idempotency tests.
    /// </summary>
    /// <param name="ingressTopic">The ingress topic name.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressTopic)
    {
        var connection = KafkaIngressTestSupport.CreateConnection(_fixture.TransportOptions);

        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                    inbox.UseInMemoryStorage();

                    inbox.UseKafkaIngress(ingress =>
                    {
                        KafkaIngressTestSupport.ConfigureTestIngress(ingress);

                        ingress.UseOptions(new KafkaInboxIngressOptions
                        {
                            Destination = ingressTopic,
                            PrefetchCount = 1,
                            Connection = connection
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
