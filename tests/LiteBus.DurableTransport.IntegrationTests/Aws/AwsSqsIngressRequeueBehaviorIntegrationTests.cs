using System.Text.Json;
using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Aws;
using LiteBus.Inbox.Ingress.Aws;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Aws;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Verifies <see cref="AwsSqsInboxIngressOptions.RequeueOnFailure" /> behavior for SQS ingress.
/// </summary>
[Collection(LocalStackSqsCollection.Name)]
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsIngressRequeueBehaviorIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "orders.commands.ship";

    /// <summary>
    ///     The shared LocalStack SQS fixture.
    /// </summary>
    private readonly LocalStackSqsFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsIngressRequeueBehaviorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LocalStack SQS fixture.</param>
    public AwsSqsIngressRequeueBehaviorIntegrationTests(LocalStackSqsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies poison messages drain the queue when requeue is disabled.
    /// </summary>
    /// <returns>A task that completes when the queue depth assertion succeeds.</returns>
    [Fact]
    public async Task RequeueDisabled_WithPoisonMessage_ShouldDrainQueue()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-requeue-off");
        await using var provider = BuildProvider(ingressQueueUrl, requeueOnFailure: false);
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
                Body = "{not-json"u8.ToArray(),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

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
    ///     Verifies transient store failures requeue when requeue is enabled.
    /// </summary>
    /// <returns>A task that completes when the store eventually accepts the delivery.</returns>
    [Fact]
    public async Task RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept()
    {
        var ingressQueueUrl = await _fixture.CreateQueueAsync("ingress-requeue-on");
        await using var provider = BuildProvider(ingressQueueUrl, requeueOnFailure: true, useFlakyInbox: true);
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IMessageTransport>();
            var messageId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = ingressQueueUrl,
                Body = JsonSerializer.SerializeToUtf8Bytes(new ShipOrderCommand { OrderId = orderId }),
                MessageId = messageId.ToString("D"),
                Headers = TransportTestHeaders.Create(messageId, ContractName, 1)
            });

            await PollingWait.UntilAsync(
                () => provider.GetRequiredService<InMemoryInboxStore>().Count == 1,
                TimeSpan.FromSeconds(45));
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider for SQS requeue behavior tests.
    /// </summary>
    /// <param name="ingressQueueUrl">The ingress queue URL.</param>
    /// <param name="requeueOnFailure">The requeue policy under test.</param>
    /// <param name="useFlakyInbox">Whether to decorate <see cref="IInbox" /> with <see cref="FlakyInbox" />.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(
        string ingressQueueUrl,
        bool requeueOnFailure,
        bool useFlakyInbox = false)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>(ContractName, 1);
                inbox.UseInMemoryStorage();
                inbox.UseAwsSqsDispatch(_ => { }, CreateTestTransportOptions());
                inbox.UseAwsSqsIngress(ingress =>
                {
                    ingress.UseOptions(new AwsSqsInboxIngressOptions
                    {
                        Destination = ingressQueueUrl,
                        PrefetchCount = 1,
                        Connection = CreateTestTransportOptions(),
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
                var inner = new global::LiteBus.Inbox.Inbox(store, contracts, serializer, clock);
                return new FlakyInbox(inner, new IOException("transient store failure"));
            });
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds transport options tuned for fast SQS requeue integration tests.
    /// </summary>
    /// <returns>Transport options with a short requeue visibility timeout.</returns>
    private AwsSqsTransportOptions CreateTestTransportOptions() => new()
    {
        ServiceUrl = _fixture.TransportOptions.ServiceUrl,
        Region = _fixture.TransportOptions.Region,
        AccessKey = _fixture.TransportOptions.AccessKey,
        SecretKey = _fixture.TransportOptions.SecretKey,
        RequeueVisibilityTimeoutSeconds = 2,
        LongPollWaitTimeSeconds = 1,
        VisibilityTimeoutSeconds = 5
    };
}
