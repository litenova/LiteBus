using Azure.Messaging.ServiceBus;
using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.AzureServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Ingress.AzureServiceBus;

/// <summary>
///     Optional Azure Service Bus integration tests driven by environment configuration.
/// </summary>
[Trait("Category", TransportTestTraits.LiveAzure)]
public sealed class AzureServiceBusOptionalIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Environment variable carrying a live Azure Service Bus connection string for optional integration tests.
    /// </summary>
    private const string ConnectionStringEnvironmentVariable = "LITEBUS_TEST_AZURE_SERVICEBUS_CONNECTION_STRING";

    /// <summary>
    ///     Environment variable carrying the queue name used for optional Azure dispatch integration tests.
    /// </summary>
    private const string QueueEnvironmentVariable = "LITEBUS_TEST_AZURE_SERVICEBUS_QUEUE";

    /// <summary>
    ///     Verifies inbox dispatch publishes to Azure Service Bus when a live connection string is configured.
    /// </summary>
    /// <returns>A task that completes when the optional integration assertion succeeds.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_WithLiveConnection_ShouldPublishToAzureServiceBusQueue()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var queueName = Environment.GetEnvironmentVariable(QueueEnvironmentVariable);
        Skip.If(string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(queueName));

        var transportOptions = new AzureServiceBusTransportOptions { ConnectionString = connectionString! };
         var provider = BuildProvider(transportOptions, queueName!);
         await using (provider.ConfigureAwait(false))
         {

        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();

        await inbox.AcceptAsync(new InboxAcceptItem<RemoteWorkCommand>
        {
            Message = new RemoteWorkCommand
            {
                WorkItemId = workItemId,
                IdempotencyKey = $"work:{workItemId}"
            }
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

         var client = new ServiceBusClient(connectionString!);
         await using (client.ConfigureAwait(false))
         {
         var receiver = client.CreateReceiver(queueName!);
         await using (receiver.ConfigureAwait(false))
         {
        var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        received.Should().NotBeNull();
        received!.Body.ToString().Should().Contain(workItemId.ToString());
        }
        }
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for optional Azure Service Bus dispatch tests.
    /// </summary>
    /// <param name="transportOptions">The Azure Service Bus connection settings.</param>
    /// <param name="queueName">The queue name used for dispatch.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(AzureServiceBusTransportOptions transportOptions, string queueName)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>("tests.remote-work");

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "azure-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();

                    inbox.UseAzureServiceBusDispatch(
                        transport => transport.DefaultDestination = queueName,
                        transportOptions);
                });
            })
            .BuildServiceProvider();
    }
}
