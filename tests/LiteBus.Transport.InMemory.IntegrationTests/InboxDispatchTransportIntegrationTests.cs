using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Transport.InMemory.IntegrationTests;

/// <summary>
///     End-to-end inbox transport dispatch tests using the in-memory transport adapter.
/// </summary>
public sealed class InboxDispatchTransportIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes payload and headers through in-memory transport.
    /// </summary>
    /// <returns>A task that completes when the publish assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination()
    {
        var destination = InMemoryTransportTestInfrastructure.CreateDestination("inbox-dispatch");
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildProvider(destination);
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
        await using var consumer = await InMemoryTransportTestInfrastructure.StartReceiveOneAsync(
            broker,
            destination,
            received);

        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();
        var receipt = await inbox.AcceptAsync(new RemoteWorkCommand
        {
            WorkItemId = workItemId,
            IdempotencyKey = $"work:{workItemId}"
        }, new InboxOptions
        {
            CorrelationId = "corr-dispatch",
            CausationId = "cause-dispatch",
            TenantId = "tenant-dispatch"
        });

        await processor.ProcessPendingAsync();

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = await received.Task.WaitAsync(cancellationSource.Token);

        InMemoryTransportTestInfrastructure.ReadBody(message).Should().Contain(workItemId.ToString());
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.MessageId)
            .Should().Be(receipt.Id.ToString("D"));
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.ContractName)
            .Should().Be(ContractName);
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.ContractVersion)
            .Should().Be(ContractVersion.ToString());
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.CorrelationId)
            .Should().Be("corr-dispatch");
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.CausationId)
            .Should().Be("cause-dispatch");
        InMemoryTransportTestInfrastructure.GetHeader(message, TransportHeaders.TenantId)
            .Should().Be("tenant-dispatch");

        var store = provider.GetRequiredService<InMemoryInboxStore>();
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for in-memory inbox dispatch tests.
    /// </summary>
    /// <param name="destination">The in-memory destination used for dispatch.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string destination)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName, ContractVersion);
                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "inmemory-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch(transport => transport.DefaultDestination = destination);
                });
            })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }
}
