using System.Text;
using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using LiteBus.Outbox;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.Amqp;

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
    ///     Verifies that the outbox processor publishes a stored envelope to the configured AMQP queue.
    /// </summary>
    /// <returns>A task that completes when the end-to-end flow succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue()
    {
        var queueName = CreateUniqueName("dispatch");
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await DeclareDirectQueueAsync(queueName).ConfigureAwait(false);

         var provider = BuildProvider(string.Empty);
         await using (provider.ConfigureAwait(false))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
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
                    Trace = new MessageTrace.Workflow("corr-outbox-amqp", "cause-outbox-amqp"),
                    Tenant = new TenantScope.Isolated("tenant-east"),
                    Target = new PublicationTarget.Topic(queueName)
                })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);

        var amqpMessage = await ConsumeOneAsync(queueName).ConfigureAwait(false);

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

        await DeclareTopicBindingAsync(exchangeName, queueName, routingKey).ConfigureAwait(false);

         var provider = BuildProvider(exchangeName);
         await using (provider.ConfigureAwait(false))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

        var amqpMessage = await ConsumeOneAsync(queueName).ConfigureAwait(false);
        amqpMessage.RoutingKey.Should().Be(routingKey);
        }
    }

    /// <summary>
    ///     Verifies shutdown cancellation after a confirmed broker publish follows the configured terminal-persist policy.
    /// </summary>
    /// <param name="honorShutdownTokenOnPersist">Whether terminal persistence should receive the shutdown token.</param>
    /// <returns>A task that completes when publication and persistence behavior are verified.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessPendingAsync_WhenShutdownBeginsAfterAmqpPublish_ShouldApplyTerminalPersistPolicy(
        bool honorShutdownTokenOnPersist)
    {
        var queueName = CreateUniqueName("shutdown-persist");
        var messageId = Guid.NewGuid();

        await DeclareDirectQueueAsync(queueName).ConfigureAwait(false);

        var provider = BuildProvider(string.Empty);
        await using (provider.ConfigureAwait(false))
        {
            using var shutdownSource = new CancellationTokenSource();
            var store = new InMemoryOutboxStore();
            var stateWriter = new TokenCapturingOutboxStateWriter(store);
            var dispatcher = new CancelAfterDispatchOutboxDispatcher(
                provider.GetRequiredService<IOutboxDispatcher>(),
                shutdownSource);
            var processor = new PipelinedOutboxProcessor(
                store,
                stateWriter,
                dispatcher,
                new OutboxProcessorOptions
                {
                    BatchSize = 1,
                    DispatcherConcurrency = 1,
                    LeaseOwner = $"outbox-amqp-shutdown-{BrokerName}",
                    LeaseDuration = TimeSpan.FromSeconds(10),
                    LeaseHeartbeatInterval = TimeSpan.Zero,
                    HonorShutdownTokenOnPersist = honorShutdownTokenOnPersist,
                    Retry = new RetryOptions { UseJitter = false }
                },
                TimeProvider.System,
                []);

            await store.AddAsync(new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.order-submitted",
                ContractVersion = 1,
                Payload = "{\"orderId\":\"11111111-1111-1111-1111-111111111111\"}",
                Topic = queueName,
                CreatedAt = DateTimeOffset.UtcNow,
                AttemptCount = 0,
                Status = OutboxStatus.Pending
            }).ConfigureAwait(false);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => processor.ProcessPendingAsync(shutdownSource.Token)).ConfigureAwait(false);

            dispatcher.DispatchCompleted.Should().BeTrue();
            shutdownSource.IsCancellationRequested.Should().BeTrue();
            stateWriter.LastPersistToken.Should().Be(
                honorShutdownTokenOnPersist ? shutdownSource.Token : CancellationToken.None);
            store.Get(messageId).Status.Should().Be(OutboxStatus.Published);

            var message = await ConsumeOneAsync(queueName).ConfigureAwait(false);
            message.MessageId.Should().Be(messageId.ToString("D"));
        }
    }

    /// <summary>
    ///     Builds the LiteBus service provider used by the end-to-end tests.
    /// </summary>
    /// <param name="exchangeName">The exchange name passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string exchangeName)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = $"outbox-amqp-{BrokerName}",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseAmqpDispatch(
                        transport => transport.DefaultDestination = exchangeName, ConnectionOptions);
                });
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
         var manager = new AmqpConnectionManager(ConnectionOptions);
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

    /// <summary>
    ///     Declares a topic exchange, queue, and binding for contract-name routing tests.
    /// </summary>
    /// <param name="exchangeName">The topic exchange name.</param>
    /// <param name="queueName">The queue bound to the exchange.</param>
    /// <param name="routingKey">The routing key used for the binding and publication.</param>
    /// <returns>A task that completes when the topology is ready.</returns>
    private async Task DeclareTopicBindingAsync(string exchangeName, string queueName, string routingKey)
    {
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
         var channel = await manager.CreateChannelAsync().ConfigureAwait(false);
         await using (channel.ConfigureAwait(false))
         {

        await channel.ExchangeDeclareAsync(
            exchangeName,
            ExchangeType.Topic,
            true,
            false,
            null).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queueName,
            true,
            false,
            false,
            null).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queueName,
            exchangeName,
            routingKey).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Consumes one message from the supplied queue and acknowledges it before returning.
    /// </summary>
    /// <param name="queueName">The queue to read from.</param>
    /// <returns>The received AMQP message with a copied body safe for use after the consumer disposes.</returns>
    private async Task<ConsumedAmqpMessage> ConsumeOneAsync(string queueName)
    {
         var manager = new AmqpConnectionManager(ConnectionOptions);
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
    ///     Cancels the processor token immediately after the inner AMQP dispatcher confirms publication.
    /// </summary>
    private sealed class CancelAfterDispatchOutboxDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     Gets the broker-backed dispatcher under test.
        /// </summary>
        private readonly IOutboxDispatcher _inner;

        /// <summary>
        ///     Gets the source canceled after broker publication completes.
        /// </summary>
        private readonly CancellationTokenSource _shutdownSource;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CancelAfterDispatchOutboxDispatcher" /> class.
        /// </summary>
        /// <param name="inner">The broker-backed dispatcher under test.</param>
        /// <param name="shutdownSource">The source canceled after dispatch completes.</param>
        public CancelAfterDispatchOutboxDispatcher(
            IOutboxDispatcher inner,
            CancellationTokenSource shutdownSource)
        {
            _inner = inner;
            _shutdownSource = shutdownSource;
        }

        /// <summary>
        ///     Gets a value indicating whether the inner dispatcher completed before shutdown was requested.
        /// </summary>
        public bool DispatchCompleted { get; private set; }

        /// <inheritdoc />
        public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            await _inner.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
            DispatchCompleted = true;
            await _shutdownSource.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Captures the token selected for terminal persistence after broker publication.
    /// </summary>
    private sealed class TokenCapturingOutboxStateWriter : IOutboxStateWriter
    {
        /// <summary>
        ///     Gets the store that applies the terminal state after the token is captured.
        /// </summary>
        private readonly IOutboxStateWriter _inner;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TokenCapturingOutboxStateWriter" /> class.
        /// </summary>
        /// <param name="inner">The store that applies terminal state.</param>
        public TokenCapturingOutboxStateWriter(IOutboxStateWriter inner)
        {
            _inner = inner;
        }

        /// <summary>
        ///     Gets the token supplied to the most recent terminal persistence call.
        /// </summary>
        public CancellationToken LastPersistToken { get; private set; }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            LastPersistToken = cancellationToken;
            return _inner.PersistAsync(envelopes, CancellationToken.None);
        }
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
[Trait("Category", TransportTestTraits.Docker)]
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
}

/// <summary>
///     Outbox AMQP dispatch tests against LavinMQ.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
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
}

/// <summary>
///     Registration tests that do not require a running AMQP broker.
/// </summary>
[Collection("Sequential")]
public sealed class AmqpOutboxDispatchRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the canonical AMQP registration extension resolves the transport dispatcher.
    /// </summary>
    [Fact]
    public void UseAmqpDispatch_WithAmqpTransportModule_ShouldRegisterTransportOutboxDispatcher()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    outbox.UseInMemoryStorage();

                    outbox.UseAmqpDispatch(
                        _ =>
                        {
                        }, new AmqpConnectionOptions { HostName = "localhost" });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<TransportOutboxDispatcher>();
    }
}
