using System.Collections.Generic;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     End-to-end tests for PostgreSQL inbox storage with AMQP ingress and in-process dispatch.
/// </summary>
public sealed class PostgreSqlInboxIngressEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _postgresFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxIngressEndToEndTests" /> class.
    /// </summary>
    /// <param name="postgresFixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlInboxIngressEndToEndTests(PostgreSqlFixture postgresFixture)
    {
        _postgresFixture = postgresFixture;
    }

    /// <summary>
    ///     Verifies that publishing through RabbitMQ stores the command in PostgreSQL and dispatches it in-process.
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
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, options);

        var queueName = $"litebus.inbox.pg.ingress.{Guid.NewGuid():N}";
        var recorder = new CommandRecorder();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.AddLiteBus(modules =>
        {
            modules.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_postgresFixture.DataSource);
                postgres.UseOptions(options);
                postgres.DisableSchemaInitialization();
            });

            modules.AddCommandModule(module =>
            {
                module.Register<ShipOrderCommand>();
                module.Register<ShipOrderCommandHandler>();
            });

            modules.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-ingress-test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
            });

            modules.AddInboxInProcessDispatcher();

            modules.AddInboxAmqpIngress(ingress =>
            {
                ingress.UseOptions(new AmqpInboxIngressOptions
                {
                    QueueName = queueName,
                    PrefetchCount = 1,
                    Connection = connectionOptions
                });
            });
        });

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().HaveCount(2);

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(runCts.Token);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var publisher = provider.GetRequiredService<IAmqpPublisher>();
            var command = new ShipOrderCommand { OrderId = orderId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

            await publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = payload,
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AmqpHeaders.MessageId] = messageId.ToString("D"),
                    [AmqpHeaders.ContractName] = "orders.commands.ship",
                    [AmqpHeaders.ContractVersion] = "1"
                }
            });

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (recorder.Commands.Any(recorded => recorded.OrderId == orderId))
                {
                    break;
                }

                await Task.Delay(100);
            }

            recorder.Commands.Should().ContainSingle(recorded => recorded.OrderId == orderId);

            var row = await PostgreSqlTableReaders.ReadInboxAsync(_postgresFixture.DataSource, options, messageId);
            row.Should().NotBeNull();
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(1);
        }
        finally
        {
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }
}
