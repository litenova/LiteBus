using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Aws;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Aws;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     End-to-end SQS ingress tests that verify store, processor, and transport dispatch.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
public sealed class AwsSqsInboxIngressEndToEndIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxIngressEndToEndIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsInboxIngressEndToEndIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies SQS ingress accepts, processes, and dispatches a command.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress");
        var dispatchQueueUrl = await _fixture.CreateQueueAsync("dispatch");
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using var provider = BuildProvider(ingressQueueUrl, dispatchQueueUrl);
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = payload,
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            var (body, headers) = await SqsTransportTestInfrastructure.ReceiveOneAsync(
                _fixture.SqsClient,
                dispatchQueueUrl,
                TimeSpan.FromSeconds(30));

            body.Should().Contain(orderId.ToString());
            headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
            headers[TransportHeaders.ContractName].Should().Be(ContractName);

            var store = provider.GetRequiredService<InMemoryInboxStore>();
            await PollingWait.UntilAsync(
                () => store.Get(messageId).Status == InboxStatus.Completed,
                TimeSpan.FromSeconds(15));
            store.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for SQS ingress end-to-end tests.
    /// </summary>
    /// <param name="ingressQueueUrl">The ingress queue URL.</param>
    /// <param name="dispatchQueueUrl">The dispatch queue URL.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ingressQueueUrl, string dispatchQueueUrl)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName, 1);
                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "sqs-ingress-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                    inbox.UseInMemoryStorage();
                    inbox.UseAwsSqsDispatch(
                        transport => transport.DefaultDestination = dispatchQueueUrl,
                        _fixture.TransportOptions);
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
