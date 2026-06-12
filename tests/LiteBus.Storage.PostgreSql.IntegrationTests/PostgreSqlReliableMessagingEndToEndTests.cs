using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Amqp;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     End-to-end reliable-messaging tests covering outbox publish, AMQP broker delivery, inbox ingress, and in-process
///     dispatch.
/// </summary>
/// <remarks>
///     Dispatch mode is explicit: <see cref="InboxModuleBuilderCommandDispatchExtensions.UseInProcessDispatch" /> runs handlers
///     locally.
///     v6 does not allow combining that with <c>UseAmqpDispatch</c> on the inbox axis; transport dispatch is covered
///     separately
///     in <see cref="PostgreSqlInboxIngressEndToEndTests" />.
/// </remarks>
public sealed class PostgreSqlReliableMessagingEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string ContractName = "orders.commands.ship";
    private static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(60);

    private readonly PostgreSqlFixture _postgresFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlReliableMessagingEndToEndTests" /> class.
    /// </summary>
    /// <param name="postgresFixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlReliableMessagingEndToEndTests(PostgreSqlFixture postgresFixture)
    {
        _postgresFixture = postgresFixture;
    }

    /// <summary>
    ///     Verifies the full outbox-to-inbox chain: PostgreSQL outbox, RabbitMQ broker, PostgreSQL inbox, and local handler
    ///     dispatch.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task OutboxToInbox_ShouldPublishProcessAndDispatchCommand()
    {
        var rabbitMqFixture = new RabbitMqBrokerFixture();
        await rabbitMqFixture.InitializeAsync();

        try
        {
            await RunReliableMessagingChainAsync(rabbitMqFixture.ConnectionOptions);
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies that a duplicate broker delivery with the same message identifier executes the handler only once.
    /// </summary>
    /// <returns>A task that completes when the idempotency assertion succeeds.</returns>
    [Fact]
    public async Task DuplicateBrokerDelivery_ShouldExecuteHandlerOnce()
    {
        var rabbitMqFixture = new RabbitMqBrokerFixture();
        await rabbitMqFixture.InitializeAsync();

        try
        {
            var ingressQueue = CreateQueueName("reliable-messaging.ingress");
            var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
            var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
            await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_postgresFixture.DataSource, outboxOptions);
            await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, inboxOptions);
            await DeclareQueueAsync(rabbitMqFixture.ConnectionOptions, ingressQueue);

            var recorder = new CommandRecorder();
            var messageId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            await using var provider = BuildReliableMessagingProvider(
                rabbitMqFixture.ConnectionOptions,
                ingressQueue,
                outboxOptions,
                inboxOptions,
                recorder);

            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

            try
            {
                var outbox = provider.GetRequiredService<IOutbox>();

                await outbox.EnqueueAsync(OutboxEnqueueItem<ShipOrderCommand>.From(
                    new ShipOrderCommand { OrderId = orderId, IdempotencyKey = $"ship:{orderId}" },
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Identity = new MessageIdentity.Supplied(messageId),
                        Target = new PublicationTarget.Topic(ingressQueue),
                        Trace = new MessageTrace.Workflow("corr-reliable-idem", "cause-reliable-idem"),
                        Tenant = new TenantScope.Isolated("tenant-reliable")
                    }));

                await WaitUntilAsync(
                    () => recorder.Commands.Count >= 1,
                    EndToEndTimeout,
                    runCts.Token);

                var outboxRow = await PostgreSqlTableReaders.ReadOutboxAsync(_postgresFixture.DataSource, outboxOptions, messageId);
                outboxRow.Should().NotBeNull();
                outboxRow!.Status.Should().Be(OutboxStatus.Published);

                var inboxRow = await PostgreSqlTableReaders.ReadInboxAsync(_postgresFixture.DataSource, inboxOptions, messageId);
                inboxRow.Should().NotBeNull();
                inboxRow!.Status.Should().Be(InboxStatus.Completed);
                inboxRow.AttemptCount.Should().Be(1);

                var payload = JsonSerializer.Serialize(new ShipOrderCommand { OrderId = orderId, IdempotencyKey = $"ship:{orderId}" });

                await PublishToIngressQueueAsync(
                    rabbitMqFixture.ConnectionOptions,
                    ingressQueue,
                    payload,
                    messageId,
                    ContractName,
                    "1",
                    "corr-reliable-idem");

                await Task.Delay(TimeSpan.FromSeconds(3), runCts.Token);

                recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

                var inboxAfterDuplicate = await PostgreSqlTableReaders.ReadInboxAsync(
                    _postgresFixture.DataSource,
                    inboxOptions,
                    messageId);

                inboxAfterDuplicate!.Status.Should().Be(InboxStatus.Completed);
                inboxAfterDuplicate.AttemptCount.Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
            }
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies that an unknown contract is discarded without requeue and does not reach the PostgreSQL inbox store.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task UnknownContract_ShouldNackWithoutRequeueAndSkipPostgreSqlStore()
    {
        var rabbitMqFixture = new RabbitMqBrokerFixture();
        await rabbitMqFixture.InitializeAsync();

        try
        {
            var ingressQueue = CreateQueueName("reliable-messaging.ingress.failures");
            var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
            await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, inboxOptions);
            await DeclareQueueAsync(rabbitMqFixture.ConnectionOptions, ingressQueue);

            await using var provider = BuildIngressOnlyProvider(
                rabbitMqFixture.ConnectionOptions,
                ingressQueue,
                inboxOptions,
                true);

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

            try
            {
                await PublishToIngressQueueAsync(
                    rabbitMqFixture.ConnectionOptions,
                    ingressQueue,
                    "{}",
                    Guid.NewGuid(),
                    "unknown.contract",
                    "1");

                await WaitForQueueDepthAsync(
                    rabbitMqFixture.ConnectionOptions,
                    ingressQueue,
                    0,
                    TimeSpan.FromSeconds(15));

                var rowCount = await PostgreSqlTableReaders.CountInboxRowsAsync(_postgresFixture.DataSource, inboxOptions);
                rowCount.Should().Be(0);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
            }
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies that invalid JSON is discarded without requeue and does not reach the PostgreSQL inbox store.
    /// </summary>
    /// <returns>A task that completes when the acknowledgement assertion succeeds.</returns>
    [Fact]
    public async Task InvalidJson_ShouldNackWithoutRequeueAndSkipPostgreSqlStore()
    {
        var rabbitMqFixture = new RabbitMqBrokerFixture();
        await rabbitMqFixture.InitializeAsync();

        try
        {
            var ingressQueue = CreateQueueName("reliable-messaging.ingress.failures");
            var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
            await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, inboxOptions);
            await DeclareQueueAsync(rabbitMqFixture.ConnectionOptions, ingressQueue);

            await using var provider = BuildIngressOnlyProvider(
                rabbitMqFixture.ConnectionOptions,
                ingressQueue,
                inboxOptions,
                true);

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

            try
            {
                await PublishToIngressQueueAsync(
                    rabbitMqFixture.ConnectionOptions,
                    ingressQueue,
                    "{not-valid-json",
                    Guid.NewGuid(),
                    ContractName,
                    "1");

                await WaitForQueueDepthAsync(
                    rabbitMqFixture.ConnectionOptions,
                    ingressQueue,
                    0,
                    TimeSpan.FromSeconds(15));

                var rowCount = await PostgreSqlTableReaders.CountInboxRowsAsync(_postgresFixture.DataSource, inboxOptions);
                rowCount.Should().Be(0);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
            }
        }
        finally
        {
            await rabbitMqFixture.DisposeAsync();
        }
    }

    /// <summary>
    ///     Runs the publish, outbox dispatch, ingress, store, processor, and handler flow.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    private async Task RunReliableMessagingChainAsync(AmqpConnectionOptions connectionOptions)
    {
        var ingressQueue = CreateQueueName("reliable-messaging.ingress");
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_postgresFixture.DataSource, outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_postgresFixture.DataSource, inboxOptions);
        await DeclareQueueAsync(connectionOptions, ingressQueue);

        var recorder = new CommandRecorder();
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string correlationId = "corr-reliable-e2e";
        const string causationId = "cause-reliable-e2e";
        const string tenantId = "tenant-reliable-e2e";

        await using var provider = BuildReliableMessagingProvider(
            connectionOptions,
            ingressQueue,
            outboxOptions,
            inboxOptions,
            recorder);

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().Contain(typeof(OutboxProcessorBackgroundService));
        manifest.BackgroundServices.Should().Contain(typeof(InboxProcessorBackgroundService));
        manifest.BackgroundServices.Should().Contain(typeof(TransportInboxIngressConsumer));

        using var runCts = new CancellationTokenSource(EndToEndTimeout);
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), runCts.Token);

        try
        {
            var outbox = provider.GetRequiredService<IOutbox>();

            await outbox.EnqueueAsync(OutboxEnqueueItem<ShipOrderCommand>.From(
                new ShipOrderCommand { OrderId = orderId, IdempotencyKey = $"ship:{orderId}" },
                OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Target = new PublicationTarget.Topic(ingressQueue),
                    Trace = new MessageTrace.Workflow(correlationId, causationId),
                    Tenant = new TenantScope.Isolated(tenantId)
                }));

            await WaitUntilAsync(
                () => recorder.Commands.Count >= 1,
                EndToEndTimeout,
                runCts.Token);

            recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

            var outboxRow = await PostgreSqlTableReaders.ReadOutboxAsync(_postgresFixture.DataSource, outboxOptions, messageId);
            outboxRow.Should().NotBeNull();
            outboxRow!.Status.Should().Be(OutboxStatus.Published);
            outboxRow.AttemptCount.Should().Be(1);
            outboxRow.CorrelationId.Should().Be(correlationId);
            outboxRow.CausationId.Should().Be(causationId);
            outboxRow.TenantId.Should().Be(tenantId);
            outboxRow.Topic.Should().Be(ingressQueue);

            var inboxRow = await PostgreSqlTableReaders.ReadInboxAsync(_postgresFixture.DataSource, inboxOptions, messageId);
            inboxRow.Should().NotBeNull();
            inboxRow!.Status.Should().Be(InboxStatus.Completed);
            inboxRow.AttemptCount.Should().Be(1);
            inboxRow.ContractName.Should().Be(ContractName);
            inboxRow.ContractVersion.Should().Be(1);
            inboxRow.CorrelationId.Should().Be(correlationId);
            inboxRow.CausationId.Should().Be(causationId);
            inboxRow.TenantId.Should().Be(tenantId);
            inboxRow.Payload.Should().Contain(orderId.ToString());
            inboxRow.CompletedAt.Should().NotBeNull();
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Builds a single host with outbox and inbox axes wired for reliable messaging through RabbitMQ.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="ingressQueue">The queue shared as outbox topic and inbox ingress source.</param>
    /// <param name="OutboxStoreOptions">The PostgreSQL outbox store options.</param>
    /// <param name="InboxStoreOptions">The PostgreSQL inbox store options.</param>
    /// <param name="recorder">The command recorder used to observe handler execution.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildReliableMessagingProvider(
        AmqpConnectionOptions connectionOptions,
        string ingressQueue,
        PostgreSqlOutboxStoreOptions OutboxStoreOptions,
        PostgreSqlInboxStoreOptions InboxStoreOptions,
        CommandRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

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

            registry.AddOutboxModule(outbox =>
            {
                outbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(OutboxStoreOptions);
                    postgres.DisableSchemaInitialization();
                });

                outbox.Contracts.Register<ShipOrderCommand>(ContractName);

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "reliable-messaging-publisher",
                    Retry = new RetryOptions { UseJitter = false }
                });

                outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));

                outbox.UseAmqpDispatch(
                    transport => transport.DefaultDestination = string.Empty, connectionOptions);
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(InboxStoreOptions);
                    postgres.DisableSchemaInitialization();
                });

                inbox.Contracts.Register<ShipOrderCommand>(ContractName);

                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "reliable-messaging-consumer",
                    Retry = new RetryOptions { UseJitter = false }
                });

                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));
                inbox.UseInProcessDispatch();

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = connectionOptions,
                        RequeueOnFailure = true
                    });
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds an inbox-only host for ingress failure scenarios against PostgreSQL storage.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="ingressQueue">The ingress queue name.</param>
    /// <param name="InboxStoreOptions">The PostgreSQL inbox store options.</param>
    /// <param name="registerShipContract">Whether to register the ship-order contract.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildIngressOnlyProvider(
        AmqpConnectionOptions connectionOptions,
        string ingressQueue,
        PostgreSqlInboxStoreOptions InboxStoreOptions,
        bool registerShipContract)
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
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(InboxStoreOptions);
                    postgres.DisableSchemaInitialization();
                });

                if (registerShipContract)
                {
                    inbox.Contracts.Register<ShipOrderCommand>(ContractName);
                }

                inbox.UseInProcessDispatch();

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = ingressQueue,
                        PrefetchCount = 1,
                        Connection = connectionOptions,
                        RequeueOnFailure = true
                    });
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to poll.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="cancellationToken">The token used to cancel waiting.</param>
    /// <returns>A task that completes when the condition is satisfied.</returns>
    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (condition())
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        condition().Should().BeTrue($"condition was not satisfied within {timeout}");
    }

    /// <summary>
    ///     Declares a durable queue on the default direct exchange.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queueName">The queue to declare.</param>
    /// <returns>A task that completes when the queue exists.</returns>
    private static async Task DeclareQueueAsync(AmqpConnectionOptions connectionOptions, string queueName)
    {
        await using var manager = new AmqpConnectionManager(connectionOptions);
        await using var channel = await manager.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queueName,
            true,
            false,
            false,
            null);
    }

    /// <summary>
    ///     Publishes one message to the ingress queue with LiteBus AMQP headers.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queueName">The ingress queue name.</param>
    /// <param name="body">The message body.</param>
    /// <param name="messageId">The stable message identifier.</param>
    /// <param name="contractName">The contract name header value.</param>
    /// <param name="contractVersion">The contract version header value.</param>
    /// <param name="correlationId">The optional correlation identifier.</param>
    /// <returns>A task that completes when the message is published.</returns>
    private static async Task PublishToIngressQueueAsync(
        AmqpConnectionOptions connectionOptions,
        string queueName,
        string body,
        Guid messageId,
        string contractName,
        string contractVersion,
        string? correlationId = null)
    {
        await using var manager = new AmqpConnectionManager(connectionOptions);
        var publisher = new AmqpPublisher(manager);

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [AmqpHeaders.MessageId] = messageId.ToString("D"),
            [AmqpHeaders.ContractName] = contractName,
            [AmqpHeaders.ContractVersion] = contractVersion
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers[AmqpHeaders.CorrelationId] = correlationId;
        }

        await publisher.PublishAsync(new AmqpPublishRequest
        {
            Exchange = string.Empty,
            RoutingKey = queueName,
            Body = Encoding.UTF8.GetBytes(body),
            Headers = headers
        });
    }

    /// <summary>
    ///     Waits until the queue depth matches the expected count.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queueName">The queue to inspect.</param>
    /// <param name="expectedCount">The expected message count.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the queue depth matches.</returns>
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

    /// <summary>
    ///     Reads the current message count for a queue.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queueName">The queue to inspect.</param>
    /// <returns>The current queue depth.</returns>
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

    /// <summary>
    ///     Creates a unique queue name for one test run.
    /// </summary>
    /// <param name="prefix">The queue name prefix.</param>
    /// <returns>A unique queue name.</returns>
    private static string CreateQueueName(string prefix)
    {
        return $"litebus.{prefix}.{Guid.NewGuid():N}";
    }
}