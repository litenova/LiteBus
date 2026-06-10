using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Ingress.AzureServiceBus;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Azure;

/// <summary>
///     Verifies Azure Service Bus inbox ingress failure handling for poison messages.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusInboxIngressFailureIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusInboxIngressFailureIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusInboxIngressFailureIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that an unknown contract does not create inbox rows and drains the ingress queue.
    /// </summary>
    /// <returns>A task that completes when assertions succeed.</returns>
    [SkippableFact]
    public async Task UnknownContract_ShouldNotWriteToStore()
    {
        var ingressQueue = _fixture.ResolveQueue("ingress-fail");
        await RunFailureScenarioAsync(ingressQueue, "{}", "unknown.contract", 1);
        await AzureServiceBusTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.TransportOptions.ConnectionString,
            ingressQueue,
            expectedCount: 0,
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    ///     Verifies that invalid JSON does not create inbox rows and drains the ingress queue.
    /// </summary>
    /// <returns>A task that completes when assertions succeed.</returns>
    [SkippableFact]
    public async Task InvalidJson_ShouldNotWriteToStore()
    {
        var ingressQueue = _fixture.ResolveQueue("ingress-fail");
        await RunFailureScenarioAsync(ingressQueue, "{not-json", ContractName, 1);
        await AzureServiceBusTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.TransportOptions.ConnectionString,
            ingressQueue,
            expectedCount: 0,
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    ///     Verifies that a store capacity failure drains the ingress queue and keeps only the pre-filled row.
    /// </summary>
    /// <returns>A task that completes when store and queue assertions succeed.</returns>
    [SkippableFact]
    public async Task StoreFull_ShouldDrainQueueAndKeepPrefilledRow()
    {
        var ingressQueue = _fixture.ResolveQueue("ingress-store-full");
        await using var provider = BuildProvider(ingressQueue, capacity: 1);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), runCts.Token);

        try
        {
            var inbox = provider.GetRequiredService<IInbox>();
            await inbox.AcceptAsync(new ShipOrderCommand { OrderId = Guid.NewGuid() });

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

            await AzureServiceBusTransportTestInfrastructure.WaitForQueueDepthAsync(
                _fixture.TransportOptions.ConnectionString,
                ingressQueue,
                expectedCount: 0,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Runs an ingress failure scenario and asserts zero pending inbox rows.
    /// </summary>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <param name="body">The message body.</param>
    /// <param name="contractName">The contract name header.</param>
    /// <param name="contractVersion">The contract version header.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private async Task RunFailureScenarioAsync(
        string ingressQueue,
        string body,
        string contractName,
        int contractVersion)
    {
        await using var provider = BuildProvider(ingressQueue);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueue,
                Body = System.Text.Encoding.UTF8.GetBytes(body),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, contractName, contractVersion)
            });

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 0,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Azure ingress failure tests.
    /// </summary>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <param name="capacity">The optional in-memory inbox capacity.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueue, int capacity = 100)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName, 1);
                    inbox.UseInMemoryStorage(builder => builder.UseOptions(new InMemoryInboxStoreOptions
                    {
                        Capacity = capacity
                    }));
                    inbox.UseAzureServiceBusDispatch(_ => { }, _fixture.TransportOptions);
                    inbox.UseAzureServiceBusIngress(ingress =>
                    {
                        ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                        {
                            Destination = ingressQueue,
                            PrefetchCount = 1,
                            Connection = _fixture.TransportOptions,
                            RequeueOnFailure = true
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
