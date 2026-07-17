using System.Text;
using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Outbox;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.Amqp;

/// <summary>
///     End-to-end outbox AMQP dispatch tests with PostgreSQL storage.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class PostgreSqlAmqpOutboxDispatchIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>, IClassFixture<RabbitMqFixture>
{
    private const string TestSchemaName = "litebus_tests";

    private readonly PostgreSqlFixture _postgresFixture;
    private readonly RabbitMqFixture _rabbitMqFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAmqpOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="postgresFixture">The shared PostgreSQL container fixture.</param>
    /// <param name="rabbitMqFixture">The shared RabbitMQ container fixture.</param>
    public PostgreSqlAmqpOutboxDispatchIntegrationTests(PostgreSqlFixture postgresFixture, RabbitMqFixture rabbitMqFixture)
    {
        _postgresFixture = postgresFixture;
        _rabbitMqFixture = rabbitMqFixture;
    }

    /// <summary>
    ///     Verifies that the outbox processor publishes a PostgreSQL-stored envelope and marks it published.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopePublished()
    {
        var storeOptions = CreateOutboxStoreOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_postgresFixture.DataSource, storeOptions).ConfigureAwait(true);

        var queueName = CreateUniqueName("pg-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await DeclareDirectQueueAsync(queueName).ConfigureAwait(true);

         var provider = BuildProvider(storeOptions, string.Empty);
         await using (provider.ConfigureAwait(false))
         {
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
                new OrderSubmittedIntegrationEvent { OrderId = orderId },
                new OutboxEnqueueMetadata
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Idempotency = Idempotency.None.Instance,
                    Visibility = MessageVisibility.Immediate.Instance,
                    Trace = new MessageTrace.Workflow("corr-pg-outbox-amqp", "cause-pg-outbox-amqp"),
                    Tenant = new TenantScope.Isolated("tenant-pg"),
                    Target = new PublicationTarget.Topic(queueName)
                })).ConfigureAwait(true);


        await processor.ProcessPendingAsync().ConfigureAwait(true);

        var row = await ReadOutboxAsync(storeOptions, messageId).ConfigureAwait(true);
        row.Should().NotBeNull();
        row!.Value.Status.Should().Be(OutboxStatus.Published);
        row.Value.AttemptCount.Should().Be(1);

        var amqpMessage = await ConsumeOneAsync(queueName).ConfigureAwait(true);
        var json = Encoding.UTF8.GetString(amqpMessage.Body);

        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload!.OrderId.Should().Be(orderId);
        amqpMessage.MessageId.Should().Be(messageId.ToString("D"));
        AmqpHeaderValues.GetString(amqpMessage.Headers, AmqpHeaders.ContractName).Should().Be("orders.order-submitted");
        }
    }

    private ServiceProvider BuildProvider(PostgreSqlOutboxStoreOptions storeOptions, string exchangeName)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Register(new AmqpTransportModule(_rabbitMqFixture.ConnectionOptions));
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    outbox.UsePostgreSqlStorage(postgres =>
                    {
                        postgres.UseDataSource(_postgresFixture.DataSource);
                        postgres.UseOptions(storeOptions);
                        postgres.DisableSchemaInitialization();
                    });

                    outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "pg-outbox-amqp-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseAmqpDispatch(
                        transport => transport.DefaultDestination = exchangeName);
                });
            })
            .BuildServiceProvider();
    }

    private async Task DeclareDirectQueueAsync(string queueName)
    {
         var manager = new AmqpConnectionManager(_rabbitMqFixture.ConnectionOptions);
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

    private async Task<ConsumedAmqpMessage> ConsumeOneAsync(string queueName)
    {
         var manager = new AmqpConnectionManager(_rabbitMqFixture.ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {
        var received = new TaskCompletionSource<ConsumedAmqpMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(
            new AmqpConsumerOptions
            {
                QueueName = queueName,
                DeclareQueue = false
            },
            async (message, cancellationToken) =>
            {
                var bodyCopy = message.Body.ToArray();
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                received.TrySetResult(new ConsumedAmqpMessage(message, bodyCopy));
            }).ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
        }
        }
    }

    private async Task<(OutboxStatus Status, int AttemptCount)?> ReadOutboxAsync(PostgreSqlOutboxStoreOptions options, Guid messageId)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

         var connection = await _postgresFixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {

        command.CommandText = $"""
                               SELECT status, attempt_count
                               FROM {tableName}
                               WHERE message_id = @message_id;
                               """;

        command.Parameters.AddWithValue("message_id", messageId);

         var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
         await using (reader.ConfigureAwait(false))
         {

        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;

        }

        return ((OutboxStatus) reader.GetInt32(0), reader.GetInt32(1));
        }
        }
        }
    }

    private static PostgreSqlOutboxStoreOptions CreateOutboxStoreOptions()
    {
        return new PostgreSqlOutboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = $"outbox_pg_amqp_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    private static string CreateUniqueName(string suffix)
    {
        return $"litebus-outbox-pg-{suffix}-{Guid.NewGuid():N}";
    }

    private sealed record ConsumedAmqpMessage(
        string? MessageId,
        IReadOnlyDictionary<string, object?> Headers,
        byte[] Body)
    {
        public ConsumedAmqpMessage(AmqpReceivedMessage message, byte[] body)
            : this(message.MessageId, message.Headers, body)
        {
        }
    }

    /// <summary>
    ///     Integration event used by the PostgreSQL outbox AMQP dispatch test.
    /// </summary>
    public sealed record OrderSubmittedIntegrationEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public Guid OrderId { get; init; }
    }
}
