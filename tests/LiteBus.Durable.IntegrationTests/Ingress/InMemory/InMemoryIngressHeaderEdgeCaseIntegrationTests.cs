using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.InMemory;

/// <summary>
///     Verifies ingress header and contract edge cases for in-memory transport ingress.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryIngressHeaderEdgeCaseIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

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
    ///     Verifies missing contract version headers are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task MissingContractVersion_ShouldDiscardWithoutStoreWrite()
    {
        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = ContractName,
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
    ///     Verifies invalid message identifiers are discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidMessageId_ShouldDiscardWithoutStoreWrite()
    {
        await RunScenarioAsync(
            JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = ContractName,
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = "not-a-guid"
            },
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies valid JSON with the wrong CLR shape is discarded without store writes.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task WrongClrShape_ShouldDiscardWithoutStoreWrite()
    {
        var messageId = Guid.NewGuid();

        await RunScenarioAsync(
            """{"unexpectedField":1}""",
            TransportTestHeaders.Create(messageId, ContractName, 1),
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs one ingress publish scenario and asserts the resulting store count.
    /// </summary>
    /// <param name="body">The message body.</param>
    /// <param name="headers">The transport headers.</param>
    /// <param name="expectedStoreCount">The expected inbox store count.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private static async Task RunScenarioAsync(
        string body,
        IReadOnlyDictionary<string, object?> headers,
        int expectedStoreCount)
    {
        var ingressDestination = $"litebus-inmemory-header-edge-{Guid.NewGuid():N}";
         var provider = BuildProvider(ingressDestination);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressDestination,
                Body = Encoding.UTF8.GetBytes(body),
                Headers = headers
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == expectedStoreCount,
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for header edge-case ingress tests.
    /// </summary>
    /// <param name="ingressDestination">The ingress destination name.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string ingressDestination)
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
                    inbox.UseInMemoryDispatch();

                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.UseOptions(new InMemoryInboxIngressOptions
                        {
                            Destination = ingressDestination,
                            PrefetchCount = 1,
                            RequeueOnFailure = true
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
