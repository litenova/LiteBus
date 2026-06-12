using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AwsSqs;
using LiteBus.Inbox.Ingress.AwsSqs;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Verifies duplicate SQS ingress deliveries with the same message identifier are idempotent.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsIngressIdempotencyIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsIngressIdempotencyIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsIngressIdempotencyIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies duplicate ingress deliveries with the same message identifier create one inbox row.
    /// </summary>
    /// <returns>A task that completes when the idempotency assertion succeeds.</returns>
    [Fact]
    public async Task DuplicateMessageId_ShouldCreateSingleInboxRow()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-idem").ConfigureAwait(false);
        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = Guid.NewGuid() });
        var headers = TransportTestHeaders.Create(messageId, ContractName, 1);

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
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = headers
            }).ConfigureAwait(false);

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            provider.GetRequiredService<InMemoryInboxStore>().Get(messageId).Status.Should().Be(InboxStatus.Pending);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for SQS ingress idempotency tests.
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
                        ingress.UseOptions(new AwsSqsInboxIngressOptions
                        {
                            Destination = ingressQueueUrl,
                            PrefetchCount = 1,
                            Connection = _fixture.TransportOptions
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }
}
