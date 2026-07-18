using System.Text.Json;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.AzureServiceBus;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.AzureServiceBus;

/// <summary>
///     End-to-end Azure Service Bus ingress tests that verify store, processor, and transport dispatch.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusInboxIngressEndToEndIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusInboxIngressEndToEndIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusInboxIngressEndToEndIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies Azure Service Bus ingress accepts, processes, and dispatches a command.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [SkippableFact]
    public async Task PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand()
    {
        var ingressQueue = _fixture.ResolveQueue("ingress");
        var dispatchQueue = _fixture.ResolveQueue("dispatch");
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(ingressQueue, dispatchQueue);
        await using (provider.ConfigureAwait(false))
        {
            var manifest = provider.GetRequiredService<LiteBusHostManifest>();
            manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));
            manifest.DiagnosticChecks.Should().ContainSingle(descriptor =>
                descriptor.ImplementationType == typeof(AzureServiceBusConnectivityDiagnosticCheck));

            var diagnostics = await DiagnosticCheckRunner.RunAsync(
                    manifest,
                    provider,
                    failHealthWhenNoProbes: true)
                .ConfigureAwait(false);

            diagnostics.Status.Should().Be(DiagnosticAggregateStatus.Healthy);
            diagnostics.Probes.Should().ContainSingle(probe =>
                probe.Name == "transport.azure_service_bus.connectivity" &&
                probe.Status == DiagnosticStatus.Healthy);

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token)
                .ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(3), runCts.Token).ConfigureAwait(false);

            try
            {
                var publisher = provider.GetRequiredService<ITransportPublisher>();

                await publisher.PublishAsync(new TransportPublishRequest
                {
                    Destination = ingressQueue,
                    Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = orderId }),
                    MessageId = messageId.ToString("D"),
                    Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
                }).ConfigureAwait(false);

                var (body, headers) = await AzureServiceBusTransportTestInfrastructure.ReceiveOneAsync(
                    _fixture.TransportOptions.ConnectionString,
                    dispatchQueue,
                    TimeSpan.FromSeconds(45)).ConfigureAwait(false);

                body.Should().Contain(orderId.ToString());
                headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));

                await PollingWait.UntilAsync(
                    () => provider.GetRequiredService<InMemoryInboxStore>().Get(messageId).Status == InboxStatus.Completed,
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Azure ingress end-to-end tests.
    /// </summary>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <param name="dispatchQueue">The dispatch queue name.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueue, string dispatchQueue)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Register(new AzureServiceBusTransportModule(_fixture.TransportOptions with
                {
                    ConnectivityCheckTarget = new AzureServiceBusQueueDiagnosticTarget(ingressQueue)
                }));
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "azure-ingress-e2e",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                    inbox.UseInMemoryStorage();

                    inbox.UseAzureServiceBusDispatch(
                        transport => transport.DefaultDestination = dispatchQueue);

                    inbox.UseAzureServiceBusIngress(ingress =>
                    {
                        ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                        {
                            Destination = ingressQueue,
                            PrefetchCount = 1
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
