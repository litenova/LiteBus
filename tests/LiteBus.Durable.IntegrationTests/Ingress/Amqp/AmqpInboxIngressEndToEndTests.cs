using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LiteBus.Inbox;

namespace LiteBus.Durable.IntegrationTests.Ingress.Amqp;

/// <summary>
///     End-to-end AMQP ingress tests that verify store, processor, and transport dispatch.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AmqpInboxIngressEndToEndTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies the RabbitMQ ingress, processor, and transport dispatch flow.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        try
        {
            await RunEndToEndAsync(fixture.ConnectionOptions).ConfigureAwait(false);
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies the LavinMQ ingress, processor, and transport dispatch flow.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand()
    {
        var fixture = new LavinMqBrokerFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        try
        {
            await RunEndToEndAsync(fixture.ConnectionOptions).ConfigureAwait(false);
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs the publish, ingress, store, processor, and transport dispatch flow against one broker.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    private static async Task RunEndToEndAsync(AmqpConnectionOptions connectionOptions)
    {
        const string contractName = "orders.commands.ship";
        var ingressQueue = $"litebus.inbox.ingress.{Guid.NewGuid():N}";
        var dispatchQueue = $"litebus.inbox.dispatch.{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await AmqpTestInfrastructure.DeclareQueueAsync(connectionOptions, ingressQueue).ConfigureAwait(false);
        await AmqpTestInfrastructure.DeclareQueueAsync(connectionOptions, dispatchQueue).ConfigureAwait(false);

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>(contractName);

                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "ingress-test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });

                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                inbox.UseInMemoryStorage();

                inbox.UseAmqpDispatch(
                    transport =>
                    {
                        transport.DefaultDestination = string.Empty;
                        transport.ResolveRoute = _ => dispatchQueue;
                    }, connectionOptions);

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseRegisteredTransport();
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = connectionOptions
                    });
                });
            });
        });

         var provider = services.BuildServiceProvider();
         await using (provider.ConfigureAwait(false))
         {
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<IAmqpPublisher>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = ingressQueue,
                Body = payload,
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AmqpHeaders.MessageId] = messageId.ToString("D"),
                    [AmqpHeaders.ContractName] = contractName,
                    [AmqpHeaders.ContractVersion] = "1"
                }
            }).ConfigureAwait(false);

            var (body, headers) = await AmqpTestInfrastructure.ReceiveOneAsync(
                connectionOptions,
                dispatchQueue,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            body.Should().Contain(orderId.ToString());
            AmqpHeaderValues.GetString(headers, AmqpHeaders.MessageId).Should().Be(messageId.ToString("D"));
            AmqpHeaderValues.GetString(headers, AmqpHeaders.ContractName).Should().Be(contractName);

            var store = provider.GetRequiredService<InMemoryInboxStore>();
            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }
}
