using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.AzureServiceBus;

/// <summary>
///     End-to-end inbox dispatch integration tests for the Azure Service Bus transport adapter.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusInboxDispatchIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;

    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusInboxDispatchIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes payload and headers to Azure Service Bus.
    /// </summary>
    /// <returns>A task that completes when the publish assertion succeeds.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue()
    {
        var queueName = _fixture.ResolveQueue("inbox-dispatch");
         var provider = BuildProvider(queueName);
         await using (provider.ConfigureAwait(false))
         {

        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();

        var receipt = await inbox.AcceptAsync(new InboxAcceptItem<RemoteWorkCommand>
        {
            Message = new RemoteWorkCommand
            {
                WorkItemId = workItemId,
                IdempotencyKey = $"work:{workItemId}"
            },
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Workflow("corr-azure-dispatch", "cause-azure-dispatch"),
                Tenant = new TenantScope.Isolated("tenant-azure")
            }
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var (body, headers) = await AzureServiceBusTransportTestInfrastructure.ReceiveOneAsync(
            _fixture.TransportOptions.ConnectionString,
            queueName,
            TimeSpan.FromSeconds(45)).ConfigureAwait(false);

        body.Should().Contain(workItemId.ToString());
        headers[TransportHeaders.MessageId].Should().Be(receipt.Id.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be(ContractName);
        headers[TransportHeaders.ContractVersion].Should().Be(ContractVersion.ToString());
        headers[TransportHeaders.CorrelationId].Should().Be("corr-azure-dispatch");
        headers[TransportHeaders.CausationId].Should().Be("cause-azure-dispatch");
        headers[TransportHeaders.TenantId].Should().Be("tenant-azure");
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Azure inbox dispatch tests.
    /// </summary>
    /// <param name="queueName">The queue name used for dispatch.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string queueName)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName);

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "azure-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();

                    inbox.UseAzureServiceBusDispatch(
                        transport => transport.DefaultDestination = queueName,
                        _fixture.TransportOptions);
                });
            })
            .BuildServiceProvider();
    }
}
