using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     End-to-end Kafka ingress tests that verify store, processor, and transport dispatch.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
public sealed class KafkaInboxIngressEndToEndIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaInboxIngressEndToEndIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaInboxIngressEndToEndIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies Kafka ingress accepts, processes, and dispatches a command.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand()
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress");
        var dispatchTopic = KafkaTransportTestInfrastructure.CreateTopic("dispatch");

        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(
            _fixture.TransportOptions.BootstrapServers,
            ingressTopic,
            dispatchTopic);

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(ingressTopic, dispatchTopic);

        try
        {
            var manifest = provider.GetRequiredService<LiteBusHostManifest>();
            manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            var (body, headers) = await KafkaTransportTestInfrastructure.ConsumeOneAsync(
                _fixture.TransportOptions.BootstrapServers,
                dispatchTopic,
                TimeSpan.FromSeconds(30));

            body.Should().Contain(orderId.ToString());
            headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
            headers[TransportHeaders.ContractName].Should().Be(ContractName);

            var store = provider.GetRequiredService<InMemoryInboxStore>();

            await PollingWait.UntilAsync(
                () => store.Get(messageId).Status == InboxStatus.Completed,
                TimeSpan.FromSeconds(15));

            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Kafka ingress end-to-end tests.
    /// </summary>
    /// <param name="ingressTopic">The ingress topic name.</param>
    /// <param name="dispatchTopic">The dispatch topic name.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressTopic, string dispatchTopic)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "kafka-ingress-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                    inbox.UseInMemoryStorage();

                    inbox.UseKafkaDispatch(
                        transport =>
                        {
                            transport.DefaultDestination = dispatchTopic;
                            transport.ResolveRoute = _ => dispatchTopic;
                        },
                        _fixture.TransportOptions);

                    inbox.UseKafkaIngress(ingress =>
                    {
                        ingress.UseOptions(new KafkaInboxIngressOptions
                        {
                            Destination = ingressTopic,
                            PrefetchCount = 1,
                            Connection = _fixture.TransportOptions
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}