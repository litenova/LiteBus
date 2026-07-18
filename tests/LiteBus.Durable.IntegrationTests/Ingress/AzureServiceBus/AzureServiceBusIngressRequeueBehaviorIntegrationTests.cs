using LiteBus.Transport.AzureServiceBus;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Ingress.AzureServiceBus;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.AzureServiceBus;

/// <summary>
///     Verifies <see cref="AzureServiceBusInboxIngressOptions.RequeueOnFailure" /> behavior for Azure ingress.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusIngressRequeueBehaviorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusIngressRequeueBehaviorIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [SkippableFact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressQueue = _fixture.ResolveQueue("ingress-requeue-on");
         var provider = BuildProvider(ingressQueue);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(3), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<ITransportPublisher>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueue,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(45)).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for Azure requeue behavior tests.
    /// </summary>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueue)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
                registry.Modules.Register(new AzureServiceBusTransportModule(_fixture.TransportOptions));
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                inbox.UseInMemoryStorage();

                inbox.UseAzureServiceBusDispatch(_ =>
                {
                });

                inbox.UseAzureServiceBusIngress(ingress =>
                {
                    ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                    {
                        Destination = ingressQueue,
                        PrefetchCount = 1,
                        RequeueOnFailure = true
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
