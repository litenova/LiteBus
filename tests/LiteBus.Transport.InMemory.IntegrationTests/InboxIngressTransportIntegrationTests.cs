using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Transport.InMemory.IntegrationTests;

/// <summary>
///     End-to-end transport ingress tests that verify store, processor, and transport dispatch wiring.
/// </summary>
public sealed class InboxIngressTransportIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that publishing through in-memory transport accepts, processes, and dispatches a command.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughInMemoryTransport_ShouldAcceptProcessAndDispatchCommand()
    {
        const string contractName = "orders.commands.ship";
        var ingressDestination = InMemoryTransportTestInfrastructure.CreateDestination("ingress");
        var dispatchDestination = InMemoryTransportTestInfrastructure.CreateDestination("dispatch");
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var dispatchReceived = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    LeaseOwner = "ingress-inmemory-test",
                    Retry = new RetryOptions { UseJitter = false }
                });

                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(50));
                inbox.UseInMemoryStorage();
                inbox.UseInMemoryDispatch(transport => transport.DefaultDestination = dispatchDestination);
            });
        });

        services.AddSingleton<TransportInboxIngressHandler>();

        await using var provider = services.BuildServiceProvider();
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));

        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
        var ingressConsumer = new InMemoryConsumer(broker);

        await using var dispatchConsumer = await InMemoryTransportTestInfrastructure.StartReceiveOneAsync(
            broker,
            dispatchDestination,
            dispatchReceived);

        var ingressHandler = provider.GetRequiredService<TransportInboxIngressHandler>();

        await ingressConsumer.StartAsync(
            new TransportConsumerOptions { Destination = ingressDestination },
            async (message, cancellationToken) =>
            {
                await ingressHandler.AcceptAsync(message, cancellationToken);
                await message.AcceptAsync(cancellationToken);
            });

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressDestination,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [TransportHeaders.MessageId] = messageId.ToString("D"),
                    [TransportHeaders.ContractName] = contractName,
                    [TransportHeaders.ContractVersion] = 1
                }
            });

            using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var dispatched = await dispatchReceived.Task.WaitAsync(receiveTimeout.Token);

            InMemoryTransportTestInfrastructure.ReadBody(dispatched).Should().Contain(orderId.ToString());

            InMemoryTransportTestInfrastructure.GetHeader(dispatched, TransportHeaders.MessageId)
                .Should().Be(messageId.ToString("D"));

            InMemoryTransportTestInfrastructure.GetHeader(dispatched, TransportHeaders.ContractName)
                .Should().Be(contractName);

            var store = provider.GetRequiredService<InMemoryInboxStore>();
            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await ingressConsumer.StopAsync(CancellationToken.None);
            await ingressConsumer.DisposeAsync();
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }
}