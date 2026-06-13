using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
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

namespace LiteBus.Inbox.Ingress.Kafka.IntegrationTests;

/// <summary>
///     End-to-end Kafka ingress tests that verify store, processor, and transport dispatch.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
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
        Console.WriteLine($"TEST: Starting E2E test at {DateTime.UtcNow:O}");
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress");
        var dispatchTopic = KafkaTransportTestInfrastructure.CreateTopic("dispatch");

        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(
            _fixture.TransportOptions.BootstrapServers,
            ingressTopic,
            dispatchTopic).ConfigureAwait(false);

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(ingressTopic, dispatchTopic);

        try
        {
            var manifest = provider.GetRequiredService<LiteBusHostManifest>();
            manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));
            manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));

            var publisher = provider.GetRequiredService<IMessageTransport>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            Console.WriteLine("TEST: Publishing message to ingress topic...");
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            }).ConfigureAwait(false);
            Console.WriteLine("TEST: Message published");

            Console.WriteLine($"TEST: Starting end-to-end session at {DateTime.UtcNow:O}");
            try
            {
                await KafkaIngressTestSupport.StartEndToEndAsync(provider).ConfigureAwait(false);
                Console.WriteLine($"TEST: End-to-end session started at {DateTime.UtcNow:O}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TEST: EXCEPTION in StartEndToEndAsync: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"TEST: StackTrace: {ex.StackTrace}");
                throw;
            }

            var store = provider.GetRequiredService<InMemoryInboxStore>();
            var inboxBefore = store.Get(messageId);
            Console.WriteLine($"TEST: Inbox status before ConsumeOneAsync: {inboxBefore?.Status}");

            Console.WriteLine($"TEST: Waiting for dispatch message from processor at {DateTime.UtcNow:O} (timeout 30s)");
            var (body, headers) = await KafkaTransportTestInfrastructure.ConsumeOneAsync(
                _fixture.TransportOptions.BootstrapServers,
                dispatchTopic,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Console.WriteLine($"TEST: Dispatch message received at {DateTime.UtcNow:O}");

            body.Should().Contain(orderId.ToString());
            headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
            headers[TransportHeaders.ContractName].Should().Be(ContractName);

            Console.WriteLine("TEST: Waiting for inbox to mark message as completed...");
            await PollingWait.UntilAsync(
                () => store.Get(messageId).Status == InboxStatus.Completed,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            Console.WriteLine("TEST: Inbox message completed");
            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await KafkaIngressTestSupport.StopEndToEndAsync(provider).ConfigureAwait(false);
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
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
                        connection);

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