using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
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
using RabbitMQ.Client;
using LiteBus.Storage.PostgreSql;
using LiteBus.Outbox;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     End-to-end tests for PostgreSQL inbox storage with AMQP ingress and transport dispatch.
/// </summary>
public sealed class PostgreSqlInboxIngressEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _postgresFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxIngressEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
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
        await rabbitMqFixture.InitializeAsync().ConfigureAwait(true);

        try
        {
            await RunEndToEndAsync(rabbitMqFixture.ConnectionOptions).ConfigureAwait(true);
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async Task RunEndToEndAsync(AmqpConnectionOptions connectionOptions)
    {
        const string contractName = "orders.commands.ship";
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, options).ConfigureAwait(false);

        var ingressQueue = $"litebus.inbox.pg.ingress.{Guid.NewGuid():N}";
        var dispatchQueue = $"litebus.inbox.pg.dispatch.{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await DeclareQueueAsync(connectionOptions, ingressQueue).ConfigureAwait(false);
        await DeclareQueueAsync(connectionOptions, dispatchQueue).ConfigureAwait(false);

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

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

                inbox.Contracts.Register<ShipOrderCommand>(contractName);

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
                    ingress.UseRegisteredTransport();
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = connectionOptions
                    });
                });
            });
        });

         var provider = services.BuildServiceProvider();
         await using (provider.ConfigureAwait(false))
         {
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.StartupTasks.Select(task => task.Name).Should().Contain("InboxObservableMetricsInitializer");
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token).ConfigureAwait(false);

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
            }).ConfigureAwait(false);


            var body = await ReceiveOneAsync(connectionOptions, dispatchQueue, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            body.Should().Contain(orderId.ToString());

            var row = await PostgreSqlTableReaders.ReadInboxAsync(_postgresFixture.DataSource, options, messageId).ConfigureAwait(false);
            row.Should().NotBeNull();
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(1);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    private static async Task DeclareQueueAsync(AmqpConnectionOptions connectionOptions, string queueName)
    {
         var manager = new AmqpConnectionManager(connectionOptions);
         await using (manager.ConfigureAwait(false))
         {
         var channel = await manager.CreateChannelAsync().ConfigureAwait(false);
         await using (channel.ConfigureAwait(false))
         {

        await channel.QueueDeclareAsync(
            queueName,
            true,
            false,
            false,
            null).ConfigureAwait(false);

        }
        }
    }

    private static async Task<string> ReceiveOneAsync(
        AmqpConnectionOptions connectionOptions,
        string queue,
        TimeSpan timeout)
    {
        var uri = connectionOptions.Uri ??
                  new Uri(
                      $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");

        var factory = new ConnectionFactory { Uri = uri };
         var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
         await using (channel.ConfigureAwait(false))
         {

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, true).ConfigureAwait(false);

            if (result is not null)
            {
                return Encoding.UTF8.GetString(result.Body.ToArray());
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException($"No message received on queue '{queue}' within {timeout}.");
        }
        }
    }
}
