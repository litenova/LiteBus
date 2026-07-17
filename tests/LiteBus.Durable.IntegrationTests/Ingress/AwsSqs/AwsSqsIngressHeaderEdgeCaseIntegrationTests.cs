using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Dispatch.AwsSqs;
using LiteBus.Inbox.Ingress.AwsSqs;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.AwsSqs;

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
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D")
            },
            0).ConfigureAwait(false);
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
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            TransportTestHeaders.Create(messageId, ContractName, 99),
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies invalid message identifier headers are ignored and the inbox assigns a generated identifier.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidMessageId_ShouldAcceptWithGeneratedInboxId()
    {
        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = ContractName,
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = "not-a-guid"
            },
            1).ConfigureAwait(false);
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
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-header-edge").ConfigureAwait(false);
         var provider = BuildProvider(ingressQueueUrl);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = Encoding.UTF8.GetBytes(body),
                Headers = headers
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == expectedStoreCount,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
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
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                    inbox.UseInMemoryStorage();

                    inbox.UseAwsSqsDispatch(_ =>
                    {
                    }, _fixture.TransportOptions);

                    inbox.UseAwsSqsIngress(ingress =>
                    {
                        ingress.UseRegisteredTransport();
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
