using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LiteBus.DurableTransport.IntegrationTests.Amqp;

/// <summary>
///     Verifies <see cref="AmqpInboxIngressOptions.RequeueOnFailure" /> behavior for AMQP ingress.
/// </summary>
[Collection(RabbitMqBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AmqpIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared RabbitMQ broker fixture.
    /// </summary>
    private readonly RabbitMqBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpIngressRequeueBehaviorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared RabbitMQ broker fixture.</param>
    public AmqpIngressRequeueBehaviorIntegrationTests(RabbitMqBrokerFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "RabbitMQ");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [SkippableFact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressQueue = $"litebus.ingress.requeue.{Guid.NewGuid():N}";
        await DeclareQueueAsync(ingressQueue);

        await using var provider = BuildProvider(ingressQueue, true);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueue,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Declares the ingress queue on the shared RabbitMQ broker.
    /// </summary>
    /// <param name="queueName">The queue name to declare.</param>
    /// <returns>A task that completes when the queue exists.</returns>
    private async Task DeclareQueueAsync(string queueName)
    {
        var factory = new ConnectionFactory
        {
            Uri = _fixture.ConnectionOptions.Uri
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, true, false, false);
    }

    /// <summary>
    ///     Builds a LiteBus service provider for AMQP requeue behavior tests.
    /// </summary>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <param name="requeueOnFailure">The requeue policy under test.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueue, bool requeueOnFailure)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                inbox.UseInMemoryStorage();

                inbox.UseAmqpDispatch(_ =>
                {
                }, _fixture.ConnectionOptions);

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = _fixture.ConnectionOptions,
                        RequeueOnFailure = requeueOnFailure
                    });
                });
            });
        });

        services.AddSingleton<IInbox>(sp =>
        {
            var store = sp.GetRequiredService<InMemoryInboxStore>();
            var contracts = sp.GetRequiredService<IMessageContractRegistry>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var clock = sp.GetRequiredService<TimeProvider>();

            var inner = new Inbox.Inbox(
                store,
                new InboxEnvelopeFactory(contracts, serializer, clock));

            return new FlakyInbox(inner, new IOException("transient store failure"));
        });

        return services.BuildServiceProvider();
    }
}
