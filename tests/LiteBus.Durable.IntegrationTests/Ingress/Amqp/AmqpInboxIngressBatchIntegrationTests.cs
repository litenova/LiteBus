using System.Text;
using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Inbox;

namespace LiteBus.Durable.IntegrationTests.Ingress.Amqp;

/// <summary>
///     Verifies AMQP batch ingress acceptance at prefetch threshold and
///     <see cref="AmqpInboxIngressOptions.BatchMaxWait" />.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AmqpInboxIngressBatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies batch ingress flushes when prefetch threshold is reached.
    /// </summary>
    /// <returns>A task that completes when all messages are stored.</returns>
    [Fact]
    public async Task EnableBatchAccept_AtPrefetchThreshold_ShouldFlushAllMessages()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        try
        {
            const int prefetch = 3;
            var queueName = CreateQueueName();
             var provider = BuildProvider(fixture.ConnectionOptions, queueName, prefetch, TimeSpan.FromSeconds(5));
             await using (provider.ConfigureAwait(false))
             {
            await StartIngressAsync(provider).ConfigureAwait(false);

            for (var index = 0; index < prefetch; index++)
            {
                await PublishAsync(
                    fixture.ConnectionOptions,
                    queueName,
                    JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() })).ConfigureAwait(false);
            }

            await WaitForStoreCountAsync(provider, prefetch, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies partial batches flush after <see cref="AmqpInboxIngressOptions.BatchMaxWait" /> elapses.
    /// </summary>
    /// <returns>A task that completes when the partial batch is stored.</returns>
    [Fact]
    public async Task EnableBatchAccept_BeforePrefetchThreshold_ShouldFlushAfterBatchMaxWait()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        try
        {
            var queueName = CreateQueueName();
            var batchWait = TimeSpan.FromMilliseconds(400);
             var provider = BuildProvider(fixture.ConnectionOptions, queueName, 10, batchWait);
             await using (provider.ConfigureAwait(false))
             {
            await StartIngressAsync(provider).ConfigureAwait(false);

            await PublishAsync(
                fixture.ConnectionOptions,
                queueName,
                JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

            await Task.Delay(batchWait + TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

            await WaitForStoreCountAsync(provider, 1, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for AMQP batch ingress tests.
    /// </summary>
    /// <param name="connectionOptions">The AMQP connection settings.</param>
    /// <param name="queueName">The ingress queue name.</param>
    /// <param name="prefetch">The prefetch count configured on ingress.</param>
    /// <param name="batchMaxWait">The batch flush timeout.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        ushort prefetch,
        TimeSpan batchMaxWait)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Register(new AmqpTransportModule(connectionOptions));
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship");
                    inbox.UseInMemoryStorage();

                    inbox.UseAmqpDispatch(_ =>
                    {
                    });

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = queueName,
                            PrefetchCount = prefetch,
                            EnableBatchAccept = true,
                            BatchMaxWait = batchMaxWait
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Starts ingress hosted services for the supplied provider.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <returns>A task that completes when hosted services have started.</returns>
    private static async Task StartIngressAsync(ServiceProvider provider)
    {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Publishes one command payload to the ingress queue.
    /// </summary>
    /// <param name="connectionOptions">The AMQP connection settings.</param>
    /// <param name="queueName">The ingress queue name.</param>
    /// <param name="body">The JSON payload body.</param>
    /// <returns>A task that completes when the message is published.</returns>
    private static async Task PublishAsync(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        string body)
    {
         var manager = new AmqpConnectionManager(connectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());

        await publisher.PublishAsync(new AmqpPublishRequest
        {
            Exchange = string.Empty,
            RoutingKey = queueName,
            Body = Encoding.UTF8.GetBytes(body),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [AmqpHeaders.MessageId] = Guid.NewGuid().ToString(),
                [AmqpHeaders.ContractName] = "orders.commands.ship",
                [AmqpHeaders.ContractVersion] = "1"
            }
        }).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a unique queue name for the current test run.
    /// </summary>
    /// <returns>The queue name.</returns>
    private static string CreateQueueName()
    {
        return $"litebus-amqp-batch-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Waits until the inbox store reports the expected envelope count.
    /// </summary>
    /// <param name="provider">The LiteBus service provider.</param>
    /// <param name="expectedCount">The expected store count.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the count matches.</returns>
    private static async Task WaitForStoreCountAsync(ServiceProvider provider, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (provider.GetRequiredService<InMemoryInboxStore>().Count == expectedCount)
            {
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException($"Inbox store count did not reach {expectedCount} within {timeout}.");
    }
}
