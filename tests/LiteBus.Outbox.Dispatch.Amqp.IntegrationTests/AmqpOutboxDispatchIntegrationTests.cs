using System.Text;
using System.Text.Json;
using LiteBus.Amqp;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LiteBus.Outbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     End-to-end outbox dispatch tests executed against each supported AMQP broker fixture.
/// </summary>
public abstract class AmqpOutboxDispatchIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Gets the broker-specific connection options supplied by the test fixture.
    /// </summary>
    protected abstract AmqpConnectionOptions ConnectionOptions { get; }

    /// <summary>
    ///     Gets the broker name used in assertion messages.
    /// </summary>
    protected abstract string BrokerName { get; }

    /// <summary>
    ///     Gets the registration delegate used to register the AMQP outbox dispatcher.
    /// </summary>
    protected abstract Action<IModuleRegistry, AmqpConnectionOptions, string> RegisterDispatcher { get; }

    /// <summary>
    ///     Verifies that the outbox processor publishes a stored envelope to the configured AMQP queue.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue()
    {
        var queueName = CreateUniqueName("dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var store = new InMemoryOutboxStore();

        await DeclareDirectQueueAsync(queueName);

        await using var provider = BuildProvider(store, string.Empty);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.AddAsync(
            new OrderSubmittedIntegrationEvent { OrderId = orderId },
            new OutboxOptions
            {
                Id = messageId,
                Topic = queueName,
                CorrelationId = "corr-outbox-amqp",
                CausationId = "cause-outbox-amqp",
                TenantId = "tenant-east"
            });

        await processor.ProcessPendingAsync();

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);

        var amqpMessage = await ConsumeOneAsync(queueName);

        var storedPayload = store.Get(messageId).Payload;
        var json = Encoding.UTF8.GetString(amqpMessage.Body);
        json.Should().Be(storedPayload);

        var payload = JsonSerializer.Deserialize<OrderSubmittedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload!.OrderId.Should().Be(orderId);
        amqpMessage.MessageId.Should().Be(messageId.ToString("D"));
        amqpMessage.CorrelationId.Should().Be("corr-outbox-amqp");
        AmqpHeaderValues.GetString(amqpMessage.Headers, AmqpHeaders.ContractName).Should().Be("orders.order-submitted");
        AmqpHeaderValues.GetInt32(amqpMessage.Headers, AmqpHeaders.ContractVersion).Should().Be(1);
        AmqpHeaderValues.GetString(amqpMessage.Headers, AmqpHeaders.CausationId).Should().Be("cause-outbox-amqp");
        AmqpHeaderValues.GetString(amqpMessage.Headers, AmqpHeaders.TenantId).Should().Be("tenant-east");
    }

    /// <summary>
    ///     Verifies that contract-name routing is used when no topic is stored on the envelope.
    /// </summary>
    /// <returns>A task that completes when contract-name routing succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoutingKey()
    {
        const string routingKey = "orders.order-submitted";
        var exchangeName = CreateUniqueName("exchange");
        var queueName = CreateUniqueName("contract-route");
        var messageId = Guid.NewGuid();
        var store = new InMemoryOutboxStore();

        await DeclareTopicBindingAsync(exchangeName, queueName, routingKey);

        await using var provider = BuildProvider(store, exchangeName);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.AddAsync(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            new OutboxOptions { Id = messageId });

        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        var amqpMessage = await ConsumeOneAsync(queueName);
        amqpMessage.RoutingKey.Should().Be(routingKey);
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by the end-to-end tests.
    /// </summary>
    /// <param name="store">The in-memory outbox store shared by storage and assertions.</param>
    /// <param name="exchangeName">The exchange name passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(InMemoryOutboxStore store, string exchangeName)
    {
        return new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = $"outbox-amqp-{BrokerName}",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });

                RegisterDispatcher(configuration, ConnectionOptions, exchangeName);
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Declares a durable queue for default-exchange routing.
    /// </summary>
    /// <param name="queueName">The queue to declare.</param>
    /// <returns>A task that completes when the queue exists.</returns>
    private async Task DeclareDirectQueueAsync(string queueName)
    {
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        await using var channel = await manager.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    /// <summary>
    ///     Declares a topic exchange, queue, and binding for contract-name routing tests.
    /// </summary>
    /// <param name="exchangeName">The topic exchange name.</param>
    /// <param name="queueName">The queue bound to the exchange.</param>
    /// <param name="routingKey">The routing key used for the binding and publication.</param>
    /// <returns>A task that completes when the topology is ready.</returns>
    private async Task DeclareTopicBindingAsync(string exchangeName, string queueName, string routingKey)
    {
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        await using var channel = await manager.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: exchangeName,
            routingKey: routingKey);
    }

    /// <summary>
    ///     Consumes one message from the supplied queue and acknowledges it before returning.
    /// </summary>
    /// <param name="queueName">The queue to read from.</param>
    /// <returns>The received AMQP message with a copied body safe for use after the consumer disposes.</returns>
    private async Task<ConsumedAmqpMessage> ConsumeOneAsync(string queueName)
    {
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
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

    /// <summary>
    ///     Represents one consumed AMQP message with metadata and a copied body.
    /// </summary>
    /// <param name="MessageId">The AMQP message identifier.</param>
    /// <param name="CorrelationId">The AMQP correlation identifier.</param>
    /// <param name="RoutingKey">The routing key from the delivery.</param>
    /// <param name="Headers">The application headers from the delivery.</param>
    /// <param name="Body">The copied message body.</param>
    private sealed record ConsumedAmqpMessage(
        string? MessageId,
        string? CorrelationId,
        string? RoutingKey,
        IReadOnlyDictionary<string, object?> Headers,
        byte[] Body)
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ConsumedAmqpMessage" /> record.
        /// </summary>
        /// <param name="message">The received AMQP message.</param>
        /// <param name="body">The copied message body.</param>
        public ConsumedAmqpMessage(AmqpReceivedMessage message, byte[] body)
            : this(message.MessageId, message.CorrelationId, message.RoutingKey, message.Headers, body)
        {
        }
    }

    /// <summary>
    ///     Creates a unique broker-safe name for the current test run.
    /// </summary>
    /// <param name="suffix">The suffix that identifies the scenario under test.</param>
    /// <returns>A unique broker-safe name.</returns>
    private static string CreateUniqueName(string suffix)
    {
        return $"litebus-outbox-{suffix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Integration event used by the end-to-end dispatch tests.
    /// </summary>
    public sealed record OrderSubmittedIntegrationEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public Guid OrderId { get; init; }
    }
}

/// <summary>
///     Outbox AMQP dispatch tests against RabbitMQ.
/// </summary>
public sealed class RabbitMqOutboxDispatchIntegrationTests : AmqpOutboxDispatchIntegrationTests, IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RabbitMqOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared RabbitMQ container fixture.</param>
    public RabbitMqOutboxDispatchIntegrationTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;

    /// <inheritdoc />
    protected override string BrokerName => "RabbitMQ";

    /// <inheritdoc />
    protected override Action<IModuleRegistry, AmqpConnectionOptions, string> RegisterDispatcher { get; } =
        (registry, connection, exchangeName) =>
        {
            registry.AddOutboxRabbitMqDispatcher(options =>
            {
                options.Connection = connection;
                options.DefaultExchange = exchangeName;
            });
        };
}

/// <summary>
///     Outbox AMQP dispatch tests against LavinMQ.
/// </summary>
public sealed class LavinMqOutboxDispatchIntegrationTests : AmqpOutboxDispatchIntegrationTests, IClassFixture<LavinMqFixture>
{
    private readonly LavinMqFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LavinMqOutboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared LavinMQ container fixture.</param>
    public LavinMqOutboxDispatchIntegrationTests(LavinMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override AmqpConnectionOptions ConnectionOptions => _fixture.ConnectionOptions;

    /// <inheritdoc />
    protected override string BrokerName => "LavinMQ";

    /// <inheritdoc />
    protected override Action<IModuleRegistry, AmqpConnectionOptions, string> RegisterDispatcher { get; } =
        (registry, connection, exchangeName) =>
        {
            registry.AddOutboxLavinMqDispatcher(options =>
            {
                options.Connection = connection;
                options.DefaultExchange = exchangeName;
            });
        };
}

/// <summary>
///     Registration tests that do not require a running AMQP broker.
/// </summary>
[Collection("Sequential")]
public sealed class AmqpOutboxDispatchRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the canonical AMQP registration extension resolves the AMQP dispatcher.
    /// </summary>
    [Fact]
    public void AddOutboxAmqpDispatcher_ShouldRegisterAmqpOutboxDispatcher()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule();
                configuration.AddOutboxAmqpDispatcher(options =>
                {
                    options.Connection.HostName = "localhost";
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<AmqpOutboxDispatcher>();
    }
}
