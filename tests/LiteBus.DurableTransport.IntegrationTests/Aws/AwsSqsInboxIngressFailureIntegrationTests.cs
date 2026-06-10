using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Aws;
using LiteBus.Inbox.Ingress.Aws;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Verifies SQS inbox ingress failure handling for poison messages.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsInboxIngressFailureIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxIngressFailureIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsInboxIngressFailureIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that an unknown contract does not create inbox rows and drains the ingress queue.
    /// </summary>
    /// <returns>A task that completes when assertions succeed.</returns>
    [Fact]
    public async Task UnknownContract_ShouldNotWriteToStore()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-fail");
        await RunFailureScenarioAsync(ingressQueueUrl, "{}", "unknown.contract", 1);
        await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.SqsClient,
            ingressQueueUrl,
            expectedCount: 0,
            TimeSpan.FromSeconds(20));
    }

    /// <summary>
    ///     Verifies that invalid JSON does not create inbox rows and drains the ingress queue.
    /// </summary>
    /// <returns>A task that completes when assertions succeed.</returns>
    [Fact]
    public async Task InvalidJson_ShouldNotWriteToStore()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-fail");
        await RunFailureScenarioAsync(ingressQueueUrl, "{not-json", ContractName, 1);
        await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.SqsClient,
            ingressQueueUrl,
            expectedCount: 0,
            TimeSpan.FromSeconds(20));
    }

    /// <summary>
    ///     Verifies that a store capacity failure drains the ingress queue and leaves only the pre-filled row.
    /// </summary>
    /// <returns>A task that completes when store and queue assertions succeed.</returns>
    [Fact]
    public async Task StoreFull_ShouldDrainQueueAndKeepPrefilledRow()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-store-full");
        await using var provider = BuildProvider(ingressQueueUrl, capacity: 1);
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
                Destination = ingressQueueUrl,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == 1,
                TimeSpan.FromSeconds(15));

            await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
                _fixture.SqsClient,
                ingressQueueUrl,
                expectedCount: 0,
                TimeSpan.FromSeconds(20));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Runs an ingress failure scenario and asserts zero pending inbox rows.
    /// </summary>
    /// <param name="ingressQueueUrl">The ingress queue URL.</param>
    /// <param name="body">The message body.</param>
    /// <param name="contractName">The contract name header.</param>
    /// <param name="contractVersion">The contract version header.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private async Task RunFailureScenarioAsync(
        string ingressQueueUrl,
        string body,
        string contractName,
        int contractVersion)
    {
        await using var provider = BuildProvider(ingressQueueUrl);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = System.Text.Encoding.UTF8.GetBytes(body),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, contractName, contractVersion)
            });

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == 0,
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for SQS ingress failure tests.
    /// </summary>
    /// <param name="ingressQueueUrl">The ingress queue URL.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueueUrl, int capacity = 100)
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
                    inbox.UseAwsSqsDispatch(_ => { }, _fixture.TransportOptions);
                    inbox.UseAwsSqsIngress(ingress =>
                    {
                        ingress.UseOptions(new AwsSqsInboxIngressOptions
                        {
                            Destination = ingressQueueUrl,
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
