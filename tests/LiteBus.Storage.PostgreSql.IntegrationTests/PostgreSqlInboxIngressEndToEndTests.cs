using System.Collections.Generic;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     End-to-end tests for PostgreSQL inbox storage with AMQP ingress and transport dispatch.
/// </summary>
public sealed class PostgreSqlInboxIngressEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _postgresFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxIngressEndToEndTests" /> class.
    /// </summary>
    /// <param name="postgresFixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlInboxIngressEndToEndTests(PostgreSqlFixture fixture)
    {
        _postgresFixture = fixture;
    }

    /// <summary>
    ///     Verifies that publishing through RabbitMQ stores the command in PostgreSQL and dispatches it through transport.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task PublishThroughRabbitMq_ShouldStoreInPostgreSqlAndDispatchCommand()
    {
        var rabbitMqFixture = new RabbitMqBrokerFixture();
        await rabbitMqFixture.InitializeAsync();

        try
        {
            await RunEndToEndAsync(rabbitMqFixture.ConnectionOptions);
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync();
        }
    }

    private async Task RunEndToEndAsync(AmqpConnectionOptions connectionOptions)
    {
        const string contractName = "orders.commands.ship";
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, options);

        var ingressQueue = $"litebus.inbox.pg.ingress.{Guid.NewGuid():N}";
        var dispatchQueue = $"litebus.inbox.pg.dispatch.{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await DeclareQueueAsync(connectionOptions, ingressQueue);
        await DeclareQueueAsync(connectionOptions, dispatchQueue);

        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(module =>
            {
                module.Register<ShipOrderCommand>();
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(options);
                    postgres.DisableSchemaInitialization();
                });

                inbox.Contracts.Register<ShipOrderCommand>(contractName, 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-ingress-test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                inbox.UseAmqpDispatch(
                    transport =>
                    {
                        transport.DefaultDestination = string.Empty;
                        transport.ResolveRoute = _ => dispatchQueue;
                    }, connectionOptions);
                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = connectionOptions
                    });
                });
            });
        });

        await using var provider = services.BuildServiceProvider();
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.StartupTasks.Select(task => task.Name).Should().Contain("InboxObservableMetricsInitializer");
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);

        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IAmqpPublisher>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = ingressQueue,
                Body = payload,
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AmqpHeaders.MessageId] = messageId.ToString("D"),
                    [AmqpHeaders.ContractName] = contractName,
                    [AmqpHeaders.ContractVersion] = "1"
                }
            });

            var body = await ReceiveOneAsync(connectionOptions, dispatchQueue, TimeSpan.FromSeconds(30));
            body.Should().Contain(orderId.ToString());

            var row = await PostgreSqlTableReaders.ReadInboxAsync(_postgresFixture.DataSource, options, messageId);
            row.Should().NotBeNull();
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(1);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    private static async Task DeclareQueueAsync(AmqpConnectionOptions connectionOptions, string queueName)
    {
        await using var manager = new AmqpConnectionManager(connectionOptions);
        await using var channel = await manager.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    private static async Task<string> ReceiveOneAsync(
        AmqpConnectionOptions connectionOptions,
        string queue,
        TimeSpan timeout)
    {
        var uri = connectionOptions.Uri ?? new Uri(
            $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");

        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = uri };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
            {
                return System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No message received on queue '{queue}' within {timeout}.");
    }
}
