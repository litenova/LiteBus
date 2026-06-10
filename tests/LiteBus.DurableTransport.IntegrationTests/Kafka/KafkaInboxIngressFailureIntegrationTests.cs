using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     Verifies Kafka inbox ingress failure handling for poison messages.
/// </summary>
[Collection(KafkaBrokerCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class KafkaInboxIngressFailureIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared Kafka broker fixture.
    /// </summary>
    private readonly KafkaBrokerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaInboxIngressFailureIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Kafka broker fixture.</param>
    public KafkaInboxIngressFailureIntegrationTests(KafkaBrokerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that an unknown contract does not create inbox rows.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task UnknownContract_ShouldNotWriteToStore()
    {
        await RunFailureScenarioAsync("{}", "unknown.contract", 1, expectedPendingCount: 0);
    }

    /// <summary>
    ///     Verifies that a store capacity failure does not increase pending rows beyond the pre-filled store.
    /// </summary>
    /// <returns>A task that completes when store and broker assertions succeed.</returns>
    [Fact]
    public async Task StoreFull_ShouldNotIncreasePendingRowsBeyondCapacity()
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress-store-full");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, ingressTopic);

        await using var provider = BuildProvider(ingressTopic, capacity: 1);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var inbox = provider.GetRequiredService<IInbox>();
            await inbox.AcceptAsync(new ShipOrderCommand { OrderId = Guid.NewGuid() });

            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == 1,
                TimeSpan.FromSeconds(15));

            await KafkaTransportTestInfrastructure.WaitForStableStoreCountAsync(
                () => GetInboxStoreCount(provider),
                expectedCount: 1,
                stableDuration: TimeSpan.FromSeconds(2),
                timeout: TimeSpan.FromSeconds(10));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Verifies that invalid JSON does not create inbox rows.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidJson_ShouldNotWriteToStore()
    {
        await RunFailureScenarioAsync("{not-json", ContractName, 1, expectedPendingCount: 0);
    }

    /// <summary>
    ///     Runs a Kafka ingress failure scenario and asserts pending inbox rows.
    /// </summary>
    /// <param name="body">The message body.</param>
    /// <param name="contractName">The contract name header.</param>
    /// <param name="contractVersion">The contract version header.</param>
    /// <param name="expectedPendingCount">The expected pending row count.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private async Task RunFailureScenarioAsync(
        string body,
        string contractName,
        int contractVersion,
        int expectedPendingCount)
    {
        var ingressTopic = KafkaTransportTestInfrastructure.CreateTopic("ingress-fail");
        await KafkaTransportTestInfrastructure.EnsureTopicsExistAsync(_fixture.TransportOptions.BootstrapServers, ingressTopic);

        await using var provider = BuildProvider(ingressTopic);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressTopic,
                Body = System.Text.Encoding.UTF8.GetBytes(body),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, contractName, contractVersion)
            });

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == expectedPendingCount,
                TimeSpan.FromSeconds(15));

            await KafkaTransportTestInfrastructure.WaitForStableStoreCountAsync(
                () => GetInboxStoreCount(provider),
                expectedCount: expectedPendingCount,
                stableDuration: TimeSpan.FromSeconds(2),
                timeout: TimeSpan.FromSeconds(10));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Kafka ingress failure tests.
    /// </summary>
    /// <param name="ingressTopic">The ingress topic name.</param>
    /// <param name="capacity">The optional in-memory inbox capacity.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressTopic, int capacity = 100)
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
                    inbox.UseKafkaDispatch(_ => { }, _fixture.TransportOptions);
                    inbox.UseKafkaIngress(ingress =>
                    {
                        ingress.UseOptions(new KafkaInboxIngressOptions
                        {
                            Destination = ingressTopic,
                            PrefetchCount = 1,
                            Connection = _fixture.TransportOptions,
                            RequeueOnFailure = true
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Gets the total number of inbox envelopes without leasing rows.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <returns>The number of stored inbox envelopes.</returns>
    private static int GetInboxStoreCount(ServiceProvider provider)
    {
        return provider.GetRequiredService<InMemoryInboxStore>().Count;
    }
}
