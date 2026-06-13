using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Transport.IntegrationTesting.Kafka;
using LiteBus.Inbox;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     Verifies ingress header and contract edge cases for Kafka transport ingress.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaIngressHeaderEdgeCaseIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaIngressHeaderEdgeCaseIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaIngressHeaderEdgeCaseIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Kafka");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies missing contract name headers are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task MissingContractName_ShouldDiscardWithoutStoreWrite()
    {
        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D")
            },
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies wrong contract versions on registered names are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task WrongContractVersion_ShouldDiscardWithoutStoreWrite()
    {
        var messageId = Guid.NewGuid();

        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            TransportTestHeaders.Create(messageId, ContractName, 99),
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies invalid message identifier headers are ignored and the inbox assigns a generated identifier.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidMessageId_ShouldAcceptWithGeneratedInboxId()
    {
        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = ContractName,
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = "not-a-guid"
            },
            1).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs one Kafka ingress publish scenario and asserts the resulting store count.
    /// </summary>
    /// <param name="body">The message body.</param>
    /// <param name="headers">The transport headers.</param>
    /// <param name="expectedStoreCount">The expected inbox store count.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private async Task RunScenarioAsync(
        string body,
        IReadOnlyDictionary<string, object?> headers,
        int expectedStoreCount)
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress-header-edge");

        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(
            _fixture.TransportOptions.BootstrapServers,
            ingressTopic).ConfigureAwait(false);

        var provider = BuildProvider(ingressTopic);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = Encoding.UTF8.GetBytes(body),
                Headers = headers
            }).ConfigureAwait(false);

            await KafkaIngressTestSupport.StartIngressAsync(provider).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == expectedStoreCount,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            await KafkaTransportTestInfrastructure.WaitForStableStoreCountAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count,
                expectedStoreCount,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            await KafkaIngressTestSupport.StopIngressAsync(provider).ConfigureAwait(false);
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for Kafka header edge-case ingress tests.
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
                            Connection = connection,
                            RequeueOnFailure = true
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
