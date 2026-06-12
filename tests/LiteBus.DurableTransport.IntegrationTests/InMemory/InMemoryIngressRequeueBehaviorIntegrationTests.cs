using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.InMemory;

/// <summary>
///     Verifies <see cref="TransportInboxIngressOptions.RequeueOnFailure" /> behavior for in-memory ingress.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     Verifies poison messages are discarded when requeue is disabled.
    /// </summary>
    /// <returns>A task that completes when the store assertion succeeds.</returns>
    [Fact]
    public async Task RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite()
    {
        var ingressDestination = $"litebus-inmemory-requeue-{Guid.NewGuid():N}";
        await using var provider = BuildProvider(ingressDestination, false);
        await StartIngressAsync(provider);

        var publisher = provider.GetRequiredService<IMessageTransport>();
        var messageId = Guid.NewGuid();

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = ingressDestination,
            Body = "{not-json"u8.ToArray(),
            MessageId = messageId.ToString("D"),
            Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
        });

        await PollingWait.UntilAsync(
            () => GetInboxStoreCount(provider) == 0,
            TimeSpan.FromSeconds(10));
    }

    /// <summary>
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [Fact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressDestination = $"litebus-inmemory-requeue-{Guid.NewGuid():N}";
        await using var provider = BuildProvider(ingressDestination, true, true);
        await StartIngressAsync(provider);

        var publisher = provider.GetRequiredService<IMessageTransport>();
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = ingressDestination,
            Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = orderId }),
            MessageId = messageId.ToString("D"),
            Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
        });

        await PollingWait.UntilAsync(
            () => GetInboxStoreCount(provider) == 1,
            TimeSpan.FromSeconds(15));
    }

    /// <summary>
    ///     Builds a LiteBus service provider for requeue behavior tests.
    /// </summary>
    /// <param name="ingressDestination">The ingress destination name.</param>
    /// <param name="requeueOnFailure">The requeue policy under test.</param>
    /// <param name="useFlakyInbox">Whether to decorate <see cref="IInbox" /> with <see cref="FlakyInbox" />.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        string ingressDestination,
        bool requeueOnFailure,
        bool useFlakyInbox = false)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
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
                        RequeueOnFailure = requeueOnFailure
                    });
                });
            });
        });

        if (useFlakyInbox)
        {
            services.AddSingleton<IInbox>(sp =>
            {
                var store = sp.GetRequiredService<InMemoryInboxStore>();
                var contracts = sp.GetRequiredService<IMessageContractRegistry>();
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                var clock = sp.GetRequiredService<TimeProvider>();

                var inner = new Inbox.Inbox(
                    store,
                    new InboxEnvelopeFactory(contracts, serializer, clock));

                return new FlakyInbox(inner, new IOException("transient store failure"));
            });
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Starts ingress hosted services for the supplied provider.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <returns>A task that completes when hosted services have started.</returns>
    private static async Task StartIngressAsync(ServiceProvider provider)
    {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), runCts.Token);
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