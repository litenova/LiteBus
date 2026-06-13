using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     Verifies <see cref="KafkaInboxIngressOptions.RequeueOnFailure" /> behavior for Kafka ingress.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaIngressRequeueBehaviorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaIngressRequeueBehaviorIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that poison messages are discarded when requeue is disabled.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task RequeueDisabled_WithPoisonMessage_ShouldDiscard()
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("requeue-disabled");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, ingressTopic).ConfigureAwait(false);

        var provider = BuildProvider(ingressTopic, requeueOnFailure: false);

        try
        {
            await KafkaIngressTestSupport.StartIngressAsync(provider).ConfigureAwait(false);

            try
            {
                var publisher = provider.GetRequiredService<IMessageTransport>();
                var messageId = Guid.NewGuid();

                await publisher.PublishAsync(new TransportPublishRequest
                {
                    Destination = ingressTopic,
                    Body = Encoding.UTF8.GetBytes("{not-json"),
                    MessageId = messageId.ToString("D"),
                    Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
                }).ConfigureAwait(false);

                await KafkaTransportTestInfrastructure.WaitForStableStoreCountAsync(
                    () => GetInboxStoreCount(provider),
                    0,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                await KafkaIngressTestSupport.StopIngressAsync(provider).ConfigureAwait(false);
            }
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [Fact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("requeue-enabled");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, ingressTopic).ConfigureAwait(false);

        var provider = BuildProvider(ingressTopic, requeueOnFailure: true);

        try
        {
            await KafkaIngressTestSupport.StartIngressAsync(provider).ConfigureAwait(false);

            try
            {
                var publisher = provider.GetRequiredService<IMessageTransport>();
                var messageId = Guid.NewGuid();

                await publisher.PublishAsync(new TransportPublishRequest
                {
                    Destination = ingressTopic,
                    Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                    MessageId = messageId.ToString("D"),
                    Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
                }).ConfigureAwait(false);

                await PollingWait.UntilAsync(
                    () => GetInboxStoreCount(provider) == 1,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                GetInboxStoreCount(provider).Should().Be(1);
            }
            finally
            {
                await KafkaIngressTestSupport.StopIngressAsync(provider).ConfigureAwait(false);
            }
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Kafka ingress requeue behavior tests.
    /// </summary>
    /// <param name="ingressTopic">The ingress topic name.</param>
    /// <param name="requeueOnFailure">The requeue policy under test.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressTopic, bool requeueOnFailure)
    {
        var connection = KafkaIngressTestSupport.CreateConnection(_fixture.TransportOptions);

        var services = new ServiceCollection()
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
                            RequeueOnFailure = requeueOnFailure
                        });
                    });
                });
            });

        // Add FlakyInbox wrapper only when testing transient failures
        if (requeueOnFailure)
        {
            services.AddSingleton<IInbox>(sp =>
            {
                var store = sp.GetRequiredService<InMemoryInboxStore>();
                var contracts = sp.GetRequiredService<IMessageContractRegistry>();
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                var clock = sp.GetRequiredService<TimeProvider>();

                var inner = new global::LiteBus.Inbox.Inbox(
                    store,
                    new InboxEnvelopeFactory(contracts, serializer, clock));

                return new FlakyInbox(inner, new IOException("transient store failure"), failureBudget: 1);
            });
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Gets the total number of inbox envelopes without leasing rows.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <returns>The number of stored inbox envelopes.</returns>
    private static int GetInboxStoreCount(ServiceProvider provider)
    {
        return provider.GetRequiredService<InMemoryInboxStore>().Count;
    }
}
