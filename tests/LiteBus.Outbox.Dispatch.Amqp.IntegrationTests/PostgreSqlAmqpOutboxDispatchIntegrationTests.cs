using System.Text;
using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Amqp;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LiteBus.Outbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     End-to-end outbox AMQP dispatch tests with PostgreSQL storage.
/// </summary>
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
        var storeOptions = CreateOutboxOptions();
        await PostgreSqlOutboxSchema.EnsureAsync(_postgresFixture.DataSource, storeOptions);

        var queueName = CreateUniqueName("pg-dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await DeclareDirectQueueAsync(queueName);

        await using var provider = BuildProvider(storeOptions, string.Empty);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(
            new OrderSubmittedIntegrationEvent { OrderId = orderId },
            new OutboxOptions
            {
                Id = messageId,
                Topic = queueName,
                CorrelationId = "corr-pg-outbox-amqp",
                CausationId = "cause-pg-outbox-amqp",
                TenantId = "tenant-pg"
            });

        await processor.ProcessPendingAsync();

        var row = await ReadOutboxAsync(storeOptions, messageId);
        row.Should().NotBeNull();
        row!.Value.Status.Should().Be(OutboxStatus.Published);
        row.Value.AttemptCount.Should().Be(1);

        var amqpMessage = await ConsumeOneAsync(queueName);
        var json = Encoding.UTF8.GetString(amqpMessage.Body);
        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload!.OrderId.Should().Be(orderId);
        amqpMessage.MessageId.Should().Be(messageId.ToString("D"));
        AmqpHeaderValues.GetString(amqpMessage.Headers, AmqpHeaders.ContractName).Should().Be("orders.order-submitted");
    }

    private ServiceProvider BuildProvider(PostgreSqlOutboxStoreOptions storeOptions, string exchangeName)
    {
        return new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddPostgreSqlOutboxStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(storeOptions);
                    postgres.DisableSchemaInitialization();
                });

                modules.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "pg-outbox-amqp-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });

                modules.AddOutboxAmqpDispatcher(options =>
                {
                    options.Connection = _rabbitMqFixture.ConnectionOptions;
                    options.DefaultExchange = exchangeName;
                });
            })
            .BuildServiceProvider();
    }

    private async Task DeclareDirectQueueAsync(string queueName)
    {
        await using var manager = new AmqpConnectionManager(_rabbitMqFixture.ConnectionOptions);
        await using var channel = await manager.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    private async Task<ConsumedAmqpMessage> ConsumeOneAsync(string queueName)
    {
        await using var manager = new AmqpConnectionManager(_rabbitMqFixture.ConnectionOptions);
        await using var consumer = new AmqpConsumer(manager);
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
                await message.AckAsync(false, cancellationToken).ConfigureAwait(false);
                received.TrySetResult(new ConsumedAmqpMessage(message, bodyCopy));
            });

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await received.Task.WaitAsync(cancellationSource.Token);
    }

    private async Task<(OutboxStatus Status, int AttemptCount)?> ReadOutboxAsync(PostgreSqlOutboxStoreOptions options, Guid messageId)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await _postgresFixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT status, attempt_count
                               FROM {tableName}
                               WHERE message_id = @message_id;
                               """;
        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ((OutboxStatus)reader.GetInt32(0), reader.GetInt32(1));
    }

    private static PostgreSqlOutboxStoreOptions CreateOutboxOptions()
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
