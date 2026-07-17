using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
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

namespace LiteBus.Durable.IntegrationTests.Ingress.Amqp;

/// <summary>
///     Verifies <see cref="AmqpInboxIngressOptions.RequeueOnFailure" /> behavior for AMQP ingress.
/// </summary>
[Collection(RabbitMqBrokerFixture.CollectionName)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AmqpIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared RabbitMQ broker fixture.
    /// </summary>
    private readonly RabbitMqBrokerFixture _broker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpIngressRequeueBehaviorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared RabbitMQ broker fixture.</param>
    public AmqpIngressRequeueBehaviorIntegrationTests(RabbitMqBrokerFixture broker)
    {
        _broker = broker;
        DockerTestGate.EnsureBrokerAvailable(_broker.IsAvailable, "RabbitMQ");
        Skip.IfNot(_broker.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [SkippableFact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressQueue = $"litebus.ingress.requeue.{Guid.NewGuid():N}";
        await DeclareQueueAsync(ingressQueue).ConfigureAwait(false);

         var provider = BuildProvider(ingressQueue, true);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = string.Empty,
                Route = ingressQueue,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            }).ConfigureAwait(false);


            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
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
            Uri = _broker.ConnectionOptions.Uri ??
                  throw new InvalidOperationException("The AMQP test broker did not provide a connection URI.")
        };

         var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
         await using (channel.ConfigureAwait(false))
         {
        await channel.QueueDeclareAsync(queueName, true, false, false).ConfigureAwait(false);
        }
        }
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
                }, _broker.ConnectionOptions);

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseRegisteredTransport();
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = _broker.ConnectionOptions,
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

            var inner = new global::LiteBus.Inbox.Inbox(
                store,
                new InboxEnvelopeFactory(contracts, serializer, clock));

            return new FlakyInbox(inner, new IOException("transient store failure"));
        });

        return services.BuildServiceProvider();
    }
}
