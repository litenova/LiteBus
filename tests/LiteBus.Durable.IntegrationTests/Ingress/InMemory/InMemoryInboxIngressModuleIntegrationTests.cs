using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.InMemory;

/// <summary>
///     End-to-end inbox ingress tests that exercise
///     <see cref="InboxModuleBuilderInMemoryIngressExtensions.UseInMemoryIngress" />.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryInboxIngressModuleIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     Verifies that <see cref="InboxModuleBuilderInMemoryIngressExtensions.UseInMemoryIngress" /> accepts, processes, and
    ///     dispatches a command.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task UseInMemoryIngress_ShouldAcceptProcessAndDispatchCommand()
    {
        var ingressDestination = $"litebus-inmemory-ingress-{Guid.NewGuid():N}";
        var dispatchDestination = $"litebus-inmemory-dispatch-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var dispatchReceived = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

         var provider = BuildProvider(ingressDestination, dispatchDestination);
         await using (provider.ConfigureAwait(false))
         {
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));

        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
         var dispatchConsumer = await StartReceiveOneAsync(broker, dispatchDestination, dispatchReceived).ConfigureAwait(false);
         await using (dispatchConsumer.ConfigureAwait(false))
         {

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(200), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<ITransportPublisher>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressDestination,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            }).ConfigureAwait(false);

            using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var dispatched = await dispatchReceived.Task.WaitAsync(receiveTimeout.Token).ConfigureAwait(false);

            TransportMessageAssertions.ReadBody(dispatched).Should().Contain(orderId.ToString());

            TransportMessageAssertions.GetHeader(dispatched, TransportHeaders.MessageId)
                .Should().Be(messageId.ToString("D"));

            TransportMessageAssertions.GetHeader(dispatched, TransportHeaders.ContractName)
                .Should().Be(ContractName);

            var store = provider.GetRequiredService<InMemoryInboxStore>();

            await PollingWait.UntilAsync(
                () => store.Get(messageId).Status == InboxStatus.Completed,
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for in-memory ingress module tests.
    /// </summary>
    /// <param name="ingressDestination">The ingress queue name.</param>
    /// <param name="dispatchDestination">The dispatch queue name.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string ingressDestination, string dispatchDestination)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Register(new InMemoryTransportModule());
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "inmemory-ingress-module-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(50));
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch(transport => transport.DefaultDestination = dispatchDestination);

                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.UseOptions(new InMemoryInboxIngressOptions
                        {
                            Destination = ingressDestination,
                            PrefetchCount = 1
                        });
                    });
                });
            })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    /// <summary>
    ///     Starts a consumer that completes the supplied task source when one message arrives.
    /// </summary>
    /// <param name="broker">The shared in-memory broker backing the consumer.</param>
    /// <param name="destination">The destination name to subscribe to.</param>
    /// <param name="received">The task source completed with the first received message.</param>
    /// <returns>The started consumer that the caller must stop and dispose.</returns>
    private static async Task<InMemoryConsumer> StartReceiveOneAsync(
        InMemoryTransportBroker broker,
        string destination,
        TaskCompletionSource<TransportMessage> received)
    {
        var consumer = new InMemoryConsumer(broker);

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = destination },
            async (message, cancellationToken) =>
            {
                received.TrySetResult(message);
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return consumer;
    }
}
