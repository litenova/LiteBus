using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

/// <summary>
///     Tests AMQP inbox ingress failure acknowledgement behavior.
/// </summary>
public sealed class AmqpInboxIngressFailureTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that an unknown contract is discarded without requeue and does not reach the inbox store.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task UnknownContract_ShouldNackWithoutRequeueAndSkipStore()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync();

        try
        {
            var queueName = CreateQueueName();
            await using var provider = BuildProvider(fixture.ConnectionOptions, queueName, 100);
            await StartIngressAsync(provider);

            await PublishAsync(
                fixture.ConnectionOptions,
                queueName,
                "{}",
                "unknown.contract",
                "1");

            await WaitForQueueDepthAsync(fixture.ConnectionOptions, queueName, 0, TimeSpan.FromSeconds(15));

            var pending = await CountPendingInboxRowsAsync(provider);
            pending.Should().Be(0);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies that invalid JSON is discarded without requeue and does not reach the inbox store.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task InvalidJson_ShouldNackWithoutRequeueAndSkipStore()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync();

        try
        {
            var queueName = CreateQueueName();
            await using var provider = BuildProvider(fixture.ConnectionOptions, queueName, 100);
            await StartIngressAsync(provider);

            await PublishAsync(
                fixture.ConnectionOptions,
                queueName,
                "{not-valid-json",
                "orders.commands.ship",
                "1");

            await WaitForQueueDepthAsync(fixture.ConnectionOptions, queueName, 0, TimeSpan.FromSeconds(15));

            var pending = await CountPendingInboxRowsAsync(provider);
            pending.Should().Be(0);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies that a store write failure that throws <see cref="InvalidOperationException" /> is discarded without
    ///     requeue.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task StoreFull_ShouldNackWithoutRequeueWhenCapacityExceeded()
    {
        var fixture = new RabbitMqBrokerFixture();
        await fixture.InitializeAsync();

        try
        {
            var queueName = CreateQueueName();
            await using var provider = BuildProvider(fixture.ConnectionOptions, queueName, 1);
            await StartIngressAsync(provider);

            var inbox = provider.GetRequiredService<IInbox>();
            await inbox.AcceptAsync(InboxAcceptItems.From(new ShipOrderCommand { OrderId = Guid.NewGuid() }));

            await PublishAsync(
                fixture.ConnectionOptions,
                queueName,
                JsonSerializer.Serialize(new ShipOrderCommand { OrderId = Guid.NewGuid() }),
                "orders.commands.ship",
                "1");

            await WaitForQueueDepthAsync(fixture.ConnectionOptions, queueName, 0, TimeSpan.FromSeconds(15));

            var pending = await CountPendingInboxRowsAsync(provider);
            pending.Should().Be(1);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static ServiceProvider BuildProvider(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        int inboxCapacity)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CommandRecorder());

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(module =>
            {
                module.Register<ShipOrderCommand>();
                module.Register<ShipOrderCommandHandler>();
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship");

                inbox.UseInMemoryStorage(builder => builder.UseOptions(new InMemoryInboxStoreOptions
                {
                    Capacity = inboxCapacity
                }));

                inbox.UseAmqpDispatch(_ =>
                {
                }, connectionOptions);

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = queueName,
                        PrefetchCount = 1,
                        Connection = connectionOptions,
                        RequeueOnFailure = true
                    });
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private static async Task StartIngressAsync(ServiceProvider provider)
    {
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);
    }

    private static async Task PublishAsync(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        string body,
        string contractName,
        string contractVersion)
    {
        await using var manager = new AmqpConnectionManager(connectionOptions);
        var publisher = new AmqpPublisher(manager);

        await publisher.PublishAsync(new AmqpPublishRequest
        {
            Exchange = string.Empty,
            RoutingKey = queueName,
            Body = Encoding.UTF8.GetBytes(body),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [AmqpHeaders.MessageId] = Guid.NewGuid().ToString(),
                [AmqpHeaders.ContractName] = contractName,
                [AmqpHeaders.ContractVersion] = contractVersion
            }
        });
    }

    private static async Task WaitForQueueDepthAsync(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        uint expectedCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var count = await GetQueueDepthAsync(connectionOptions, queueName);

            if (count == expectedCount)
            {
                return;
            }

            await Task.Delay(200);
        }

        var actual = await GetQueueDepthAsync(connectionOptions, queueName);
        actual.Should().Be(expectedCount, $"queue '{queueName}' should reach depth {expectedCount} within {timeout}");
    }

    private static async Task<uint> GetQueueDepthAsync(AmqpConnectionOptions connectionOptions, string queueName)
    {
        var uri = connectionOptions.Uri ??
                  new Uri(
                      $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");

        var factory = new ConnectionFactory { Uri = uri };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var declare = await channel.QueueDeclarePassiveAsync(queueName);
        return declare.MessageCount;
    }

    private static async Task<int> CountPendingInboxRowsAsync(ServiceProvider provider)
    {
        var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

        var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 100,
            LeaseOwner = "ingress-failure-test",
            Now = DateTimeOffset.UtcNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        return leased.Count;
    }

    private static string CreateQueueName()
    {
        return $"litebus.inbox.ingress.failures.{Guid.NewGuid():N}";
    }
}