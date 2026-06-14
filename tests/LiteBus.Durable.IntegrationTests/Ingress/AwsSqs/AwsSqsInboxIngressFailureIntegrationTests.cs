using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AwsSqs;
using LiteBus.Inbox.Ingress.AwsSqs;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.AwsSqs;

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
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-fail").ConfigureAwait(false);
        await RunFailureScenarioAsync(ingressQueueUrl, "{}", "unknown.contract", 1).ConfigureAwait(false);

        await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.SqsClient,
            ingressQueueUrl,
            0,
            TimeSpan.FromSeconds(20)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that invalid JSON does not create inbox rows and drains the ingress queue.
    /// </summary>
    /// <returns>A task that completes when assertions succeed.</returns>
    [Fact]
    public async Task InvalidJson_ShouldNotWriteToStore()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-fail").ConfigureAwait(false);
        await RunFailureScenarioAsync(ingressQueueUrl, "{not-json", ContractName, 1).ConfigureAwait(false);

        await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
            _fixture.SqsClient,
            ingressQueueUrl,
            0,
            TimeSpan.FromSeconds(20)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a store capacity failure drains the ingress queue and leaves only the pre-filled row.
    /// </summary>
    /// <returns>A task that completes when store and queue assertions succeed.</returns>
    [Fact]
    public async Task StoreFull_ShouldDrainQueueAndKeepPrefilledRow()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-store-full").ConfigureAwait(false);
         var provider = BuildProvider(ingressQueueUrl, 1);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

        try
        {
            var inbox = provider.GetRequiredService<IInbox>();

            await inbox.AcceptAsync(new InboxAcceptItem<ShipOrderCommand>
            {
                Message = new ShipOrderCommand { OrderId = Guid.NewGuid() }
            }).ConfigureAwait(false);

            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == 1,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            await SqsTransportTestInfrastructure.WaitForQueueDepthAsync(
                _fixture.SqsClient,
                ingressQueueUrl,
                0,
                TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
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
         var provider = BuildProvider(ingressQueueUrl);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = Encoding.UTF8.GetBytes(body),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, contractName, contractVersion)
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => GetInboxStoreCount(provider) == 0,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
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
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);

                    inbox.UseInMemoryStorage(builder => builder.UseOptions(new InMemoryInboxStoreOptions
                    {
                        Capacity = capacity
                    }));

                    inbox.UseAwsSqsDispatch(_ =>
                    {
                    }, _fixture.TransportOptions);

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
