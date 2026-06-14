using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.InMemory;

/// <summary>
///     End-to-end inbox transport dispatch tests using the in-memory transport adapter.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryInboxDispatchIntegrationTests : LiteBusTestBase
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
        var destination = CreateDestination("inbox-dispatch");
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

         var provider = BuildProvider(destination);
         await using (provider.ConfigureAwait(false))
         {
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
         var consumer = await StartReceiveOneAsync(broker, destination, received).ConfigureAwait(false);
         await using (consumer.ConfigureAwait(false))
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
                Trace = new MessageTrace.Workflow("corr-dispatch", "cause-dispatch"),
                Tenant = new TenantScope.Isolated("tenant-dispatch")
            }
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);

        TransportMessageAssertions.ReadBody(message).Should().Contain(workItemId.ToString());

        TransportMessageAssertions.GetHeader(message, TransportHeaders.MessageId)
            .Should().Be(receipt.Id.ToString("D"));

        TransportMessageAssertions.GetHeader(message, TransportHeaders.ContractName)
            .Should().Be(ContractName);

        TransportMessageAssertions.GetHeader(message, TransportHeaders.ContractVersion)
            .Should().Be(ContractVersion.ToString());

        TransportMessageAssertions.GetHeader(message, TransportHeaders.CorrelationId)
            .Should().Be("corr-dispatch");

        TransportMessageAssertions.GetHeader(message, TransportHeaders.CausationId)
            .Should().Be("cause-dispatch");

        TransportMessageAssertions.GetHeader(message, TransportHeaders.TenantId)
            .Should().Be("tenant-dispatch");

        provider.GetRequiredService<InMemoryInboxStore>().Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
        }
        }
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
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName);

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

    /// <summary>
    ///     Creates a unique destination name for the current test run.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>A destination name safe for in-memory transport routing.</returns>
    private static string CreateDestination(string prefix)
    {
        return $"litebus-inmemory-{prefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Starts a consumer that completes the supplied task source when one message arrives.
    /// </summary>
    /// <param name="broker">The shared in-memory broker backing the consumer.</param>
    /// <param name="destination">The destination name to subscribe to.</param>
    /// <param name="received">The task source completed with the first received message.</param>
    /// <returns>The started consumer that the caller must stop and dispose.</returns>
    private static async Task<InMemoryConsumer> StartReceiveOneAsync(
        InMemoryTransportBroker broker,
        string destination,
        TaskCompletionSource<TransportMessage> received)
    {
        var consumer = new InMemoryConsumer(broker);

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = destination },
            async (message, cancellationToken) =>
            {
                received.TrySetResult(message);
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return consumer;
    }
}
