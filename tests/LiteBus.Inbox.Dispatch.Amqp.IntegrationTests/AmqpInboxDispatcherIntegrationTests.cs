using LiteBus.Amqp;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     End-to-end inbox dispatch tests executed against each supported AMQP broker fixture.
/// </summary>
public abstract class AmqpInboxDispatcherIntegrationTests : LiteBusTestBase
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;

    /// <summary>
    ///     Gets the broker connection options supplied by the test fixture.
    /// </summary>
    protected abstract AmqpConnectionOptions ConnectionOptions { get; }

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes the payload and headers to AMQP.
    /// </summary>
    /// <returns>A task that completes when the publish assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue()
    {
        var exchangeName = $"litebus.inbox.dispatch.{Guid.NewGuid():N}";
        var queueName = $"litebus.inbox.dispatch.queue.{Guid.NewGuid():N}";
        var routingKey = ContractName;
        var connectionUri = ResolveConnectionUri(ConnectionOptions);

        await AmqpTestInfrastructure.DeclareDirectTopologyAsync(
            connectionUri,
            exchangeName,
            queueName,
            routingKey);

        await using var provider = BuildProvider(ConnectionOptions, exchangeName, routingKey);
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();
        var receipt = await inbox.AddAsync(new RemoteWorkCommand
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

        var (body, headers) = await AmqpTestInfrastructure.ReceiveOneAsync(
            connectionUri,
            queueName,
            TimeSpan.FromSeconds(30));

        body.Should().Contain(workItemId.ToString());
        headers[AmqpHeaders.MessageId].Should().Be(receipt.Id.ToString("D"));
        headers[AmqpHeaders.ContractName].Should().Be(ContractName);
        headers[AmqpHeaders.ContractVersion].Should().Be(ContractVersion);
        headers[AmqpHeaders.CorrelationId].Should().Be("corr-dispatch");
        headers[AmqpHeaders.CausationId].Should().Be("cause-dispatch");
        headers[AmqpHeaders.TenantId].Should().Be("tenant-dispatch");
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for AMQP inbox dispatch tests.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="exchangeName">The exchange bound to the test queue.</param>
    /// <param name="routingKey">The routing key bound to the test queue.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(AmqpConnectionOptions connectionOptions, string exchangeName, string routingKey)
    {
        return new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName, ContractVersion);
                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "amqp-dispatch-test",
                        Retry = new RetryOptions
                        {
                            UseJitter = false
                        }
                    });
                });

                configuration.AddInMemoryInboxStorage();
                configuration.AddInboxAmqpDispatcher(amqp =>
                {
                    amqp.Connection = connectionOptions;
                    amqp.DefaultExchange = exchangeName;
                    amqp.ResolveRoutingKey = _ => routingKey;
                });
            })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    /// <summary>
    ///     Resolves a connection URI from AMQP connection options for test topology helpers.
    /// </summary>
    /// <param name="connectionOptions">The connection options configured for the broker fixture.</param>
    /// <returns>The AMQP URI used by RabbitMQ.Client helpers in the test project.</returns>
    private static Uri ResolveConnectionUri(AmqpConnectionOptions connectionOptions)
    {
        if (connectionOptions.Uri is not null)
        {
            return connectionOptions.Uri;
        }

        return new Uri(
            $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");
    }
}
