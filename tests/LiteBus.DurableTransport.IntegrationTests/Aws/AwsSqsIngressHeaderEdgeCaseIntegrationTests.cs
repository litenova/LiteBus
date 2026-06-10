using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Dispatch.Aws;
using LiteBus.Inbox.Ingress.Aws;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Verifies ingress header and contract edge cases for SQS transport ingress.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsIngressHeaderEdgeCaseIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsIngressHeaderEdgeCaseIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsIngressHeaderEdgeCaseIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies missing contract name headers are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task MissingContractName_ShouldDiscardWithoutStoreWrite()
    {
        await RunScenarioAsync(
            body: JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            headers: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D")
            },
            expectedStoreCount: 0);
    }

    /// <summary>
    ///     Verifies wrong contract versions on registered names are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task WrongContractVersion_ShouldDiscardWithoutStoreWrite()
    {
        var messageId = Guid.NewGuid();
        await RunScenarioAsync(
            body: JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            headers: TransportTestHeaders.Create(messageId, ContractName, 99),
            expectedStoreCount: 0);
    }

    /// <summary>
    ///     Verifies invalid message identifier headers are ignored and the inbox assigns a generated identifier.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidMessageId_ShouldAcceptWithGeneratedInboxId()
    {
        await RunScenarioAsync(
            body: JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            headers: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = ContractName,
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = "not-a-guid"
            },
            expectedStoreCount: 1);
    }

    /// <summary>
    ///     Runs one SQS ingress publish scenario and asserts the resulting store count.
    /// </summary>
    /// <param name="body">The message body.</param>
    /// <param name="headers">The transport headers.</param>
    /// <param name="expectedStoreCount">The expected inbox store count.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private async Task RunScenarioAsync(
        string body,
        IReadOnlyDictionary<string, object?> headers,
        int expectedStoreCount)
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-header-edge");
        await using var provider = BuildProvider(ingressQueueUrl);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = System.Text.Encoding.UTF8.GetBytes(body),
                Headers = headers
            });

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == expectedStoreCount,
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for SQS header edge-case ingress tests.
    /// </summary>
    /// <param name="ingressQueueUrl">The ingress queue URL.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueueUrl)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName, 1);
                    inbox.UseInMemoryStorage();
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
}
