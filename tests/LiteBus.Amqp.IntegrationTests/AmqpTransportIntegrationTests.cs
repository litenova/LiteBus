using System.Text;
using LiteBus.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Amqp.IntegrationTests;

/// <summary>
///     Shared AMQP transport tests executed against each supported broker fixture.
/// </summary>
public abstract class AmqpTransportIntegrationTests
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
    ///     Verifies that a published message can be consumed and acknowledged.
    /// </summary>
    /// <returns>A task that completes when the publish and consume flow succeeds.</returns>
    [Fact]
    public async Task PublishAsync_ThenConsume_AcknowledgesMessage()
    {
        var queueName = CreateUniqueQueueName("ack");
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        var publisher = new AmqpPublisher(manager);
        await using var consumer = new AmqpConsumer(manager);

        var received = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            });

        var body = Encoding.UTF8.GetBytes($"hello-{BrokerName}");
        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = body
            });

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var message = await received.Task.WaitAsync(cancellationSource.Token);
        message.Body.ToArray().Should().BeEquivalentTo(body);
        await message.AckAsync(false, CancellationToken.None);
    }

    /// <summary>
    ///     Verifies that negative acknowledgement with requeue delivers the message again.
    /// </summary>
    /// <returns>A task that completes when the requeue flow succeeds.</returns>
    [Fact]
    public async Task ConsumeAsync_NackWithRequeue_RedeliversMessage()
    {
        var queueName = CreateUniqueQueueName("nack");
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        var publisher = new AmqpPublisher(manager);
        await using var consumer = new AmqpConsumer(manager);

        var firstDelivery = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDelivery = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryCount = 0;

        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            async (message, token) =>
            {
                var count = Interlocked.Increment(ref deliveryCount);
                if (count == 1)
                {
                    firstDelivery.TrySetResult(message);
                    await message.NackAsync(true, false, token).ConfigureAwait(false);
                    return;
                }

                secondDelivery.TrySetResult(message);
                await message.AckAsync(false, token).ConfigureAwait(false);
            });

        var body = Encoding.UTF8.GetBytes($"retry-{BrokerName}");
        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = body
            });

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstMessage = await firstDelivery.Task.WaitAsync(cancellationSource.Token);
        firstMessage.Redelivered.Should().BeFalse();

        var secondMessage = await secondDelivery.Task.WaitAsync(cancellationSource.Token);
        secondMessage.Redelivered.Should().BeTrue();
        secondMessage.Body.ToArray().Should().BeEquivalentTo(body);
    }

    /// <summary>
    ///     Verifies that LiteBus header constants round-trip through publish and consume.
    /// </summary>
    /// <returns>A task that completes when header values are preserved.</returns>
    [Fact]
    public async Task PublishAsync_WithLiteBusHeaders_PreservesHeaderValues()
    {
        var queueName = CreateUniqueQueueName("headers");
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        var publisher = new AmqpPublisher(manager);
        await using var consumer = new AmqpConsumer(manager);

        var received = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            });

        var messageId = Guid.NewGuid().ToString("D");
        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = Encoding.UTF8.GetBytes("{}"),
                MessageId = messageId,
                CorrelationId = "corr-1",
                Headers = new Dictionary<string, object?>
                {
                    [AmqpHeaders.MessageId] = messageId,
                    [AmqpHeaders.ContractName] = "orders.order-submitted",
                    [AmqpHeaders.ContractVersion] = 1,
                    [AmqpHeaders.CorrelationId] = "corr-1",
                    [AmqpHeaders.CausationId] = "cause-1",
                    [AmqpHeaders.TenantId] = "tenant-a"
                }
            });

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var message = await received.Task.WaitAsync(cancellationSource.Token);
        message.MessageId.Should().Be(messageId);
        message.CorrelationId.Should().Be("corr-1");
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.ContractName).Should().Be("orders.order-submitted");
        AmqpHeaderValues.GetInt32(message.Headers, AmqpHeaders.ContractVersion).Should().Be(1);
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.CausationId).Should().Be("cause-1");
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.TenantId).Should().Be("tenant-a");
        await message.AckAsync(false, CancellationToken.None);
    }

    /// <summary>
    ///     Verifies that the connection manager recreates a working connection after the previous one closes.
    /// </summary>
    /// <returns>A task that completes when publish succeeds on the recreated connection.</returns>
    [Fact]
    public async Task GetConnectionAsync_AfterConnectionClosed_RecreatesWorkingConnection()
    {
        var queueName = CreateUniqueQueueName("recovery");
        await using var manager = new AmqpConnectionManager(ConnectionOptions);
        var connection = await manager.GetConnectionAsync();
        await connection.CloseAsync(
            Constants.ReplySuccess,
            "integration-test",
            TimeSpan.FromSeconds(10),
            abort: false,
            CancellationToken.None);

        var recreated = await manager.GetConnectionAsync();
        recreated.IsOpen.Should().BeTrue();

        await using var channel = await manager.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);

        var body = Encoding.UTF8.GetBytes("recovery");
        await channel.BasicPublishAsync(
            string.Empty,
            queueName,
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: body);

        var delivery = await channel.BasicGetAsync(queueName, autoAck: false);
        delivery.Should().NotBeNull();
        delivery!.Body.ToArray().Should().BeEquivalentTo(body);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
    }

    /// <summary>
    ///     Creates a unique queue name for the current test run.
    /// </summary>
    /// <param name="suffix">The suffix that identifies the scenario under test.</param>
    /// <returns>A broker-safe queue name.</returns>
    private static string CreateUniqueQueueName(string suffix)
    {
        return $"litebus-amqp-{suffix}-{Guid.NewGuid():N}";
    }
}
