using System.Text;
using System.Text.Json;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.InMemory.IntegrationTests;

/// <summary>
///     Verifies in-memory ingress failure acknowledgement behavior through
///     <see cref="InboxModuleBuilderInMemoryIngressExtensions.UseInMemoryIngress" />.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryIngressFailureIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     Verifies that an unknown contract is discarded and does not reach the inbox store.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task UnknownContract_ShouldDiscardWithoutStoreWrite()
    {
        await RunFailureScenarioAsync(
            "{}",
            "unknown.contract",
            1,
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that invalid JSON is discarded and does not reach the inbox store.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task InvalidJson_ShouldDiscardWithoutStoreWrite()
    {
        await RunFailureScenarioAsync(
            "{not-valid-json",
            ContractName,
            1,
            0).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a store capacity failure does not leave additional pending rows beyond the pre-filled store.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task StoreFull_ShouldNotIncreasePendingRowsBeyondCapacity()
    {
        var ingressDestination = $"litebus-inmemory-ingress-fail-{Guid.NewGuid():N}";

         var provider = BuildProvider(ingressDestination, 1);
         await using (provider.ConfigureAwait(false))
         {
        await StartIngressAsync(provider).ConfigureAwait(false);

        var inbox = provider.GetRequiredService<IInbox>();

        await inbox.AcceptAsync(new InboxAcceptItem<ShipOrderCommand>
        {
            Message = new ShipOrderCommand { OrderId = Guid.NewGuid() }
        }).ConfigureAwait(false);

        var publisher = provider.GetRequiredService<IMessageTransport>();
        var messageId = Guid.NewGuid();

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = ingressDestination,
            Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
            MessageId = messageId.ToString("D"),
            Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
        }).ConfigureAwait(false);

        await PollingWait.UntilAsync(
            () => GetInboxStoreCount(provider) == 1,
            TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        GetInboxStoreCount(provider).Should().Be(1);
        }
    }

    /// <summary>
    ///     Runs a publish scenario and asserts the resulting pending inbox row count.
    /// </summary>
    /// <param name="body">The message body published to ingress.</param>
    /// <param name="contractName">The contract name header value.</param>
    /// <param name="contractVersion">The contract version header value.</param>
    /// <param name="expectedPendingCount">The expected pending inbox row count.</param>
    /// <returns>A task that completes when assertions succeed.</returns>
    private static async Task RunFailureScenarioAsync(
        string body,
        string contractName,
        int contractVersion,
        int expectedPendingCount)
    {
        var ingressDestination = $"litebus-inmemory-ingress-fail-{Guid.NewGuid():N}";

         var provider = BuildProvider(ingressDestination, 100);
         await using (provider.ConfigureAwait(false))
         {
        await StartIngressAsync(provider).ConfigureAwait(false);

        var publisher = provider.GetRequiredService<IMessageTransport>();
        var messageId = Guid.NewGuid();

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = ingressDestination,
            Body = Encoding.UTF8.GetBytes(body),
            MessageId = messageId.ToString("D"),
            Headers = TransportTestHeaders.Create(messageId, contractName, contractVersion)
        }).ConfigureAwait(false);

        await PollingWait.UntilAsync(
            () => GetInboxStoreCount(provider) == expectedPendingCount,
            TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for ingress failure tests.
    /// </summary>
    /// <param name="ingressDestination">The ingress queue name.</param>
    /// <param name="capacity">The in-memory inbox capacity.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string ingressDestination, int capacity)
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

    /// <summary>
    ///     Starts ingress and processor hosted services for the supplied provider.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <returns>A task that completes when hosted services have started.</returns>
    private static async Task StartIngressAsync(ServiceProvider provider)
    {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300), runCts.Token).ConfigureAwait(false);
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
