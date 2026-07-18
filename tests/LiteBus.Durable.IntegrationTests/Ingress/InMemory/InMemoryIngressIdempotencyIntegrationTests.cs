using LiteBus.Transport.InMemory;
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

namespace LiteBus.Durable.IntegrationTests.Ingress.InMemory;

/// <summary>
///     Verifies duplicate broker deliveries with the same message identifier are idempotent.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryIngressIdempotencyIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     Verifies duplicate ingress deliveries with the same message identifier create one inbox row.
    /// </summary>
    /// <returns>A task that completes when the idempotency assertion succeeds.</returns>
    [Fact]
    public async Task DuplicateMessageId_ShouldCreateSingleInboxRow()
    {
        var ingressDestination = $"litebus-inmemory-idem-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = orderId });

         var provider = BuildProvider(ingressDestination);
         await using (provider.ConfigureAwait(false))
         {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300), runCts.Token).ConfigureAwait(false);

        try
        {
            var publisher = provider.GetRequiredService<ITransportPublisher>();
            var headers = TransportTestHeaders.Create(messageId, ContractName, 1);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressDestination,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressDestination,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            provider.GetRequiredService<InMemoryInboxStore>().Get(messageId).Status.Should().Be(InboxStatus.Pending);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for ingress idempotency tests.
    /// </summary>
    /// <param name="ingressDestination">The ingress destination name.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string ingressDestination)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new InMemoryTransportModule());
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch();

                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.UseOptions(new InMemoryInboxIngressOptions
                        {
                            Destination = ingressDestination,
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
