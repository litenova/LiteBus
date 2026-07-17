using System.Text;
using LiteBus.Transport.Amqp.Exceptions;
using RabbitMQ.Client;

namespace LiteBus.Transport.IntegrationTests.Amqp;

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
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {

        var received = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            }).ConfigureAwait(true);


        var body = Encoding.UTF8.GetBytes($"hello-{BrokerName}");

        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = body
            }).ConfigureAwait(true);


        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var message = await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(true);
        message.Body.ToArray().Should().BeEquivalentTo(body);
        await message.AcceptAsync(CancellationToken.None).ConfigureAwait(true);
        }
        }
    }

    /// <summary>
    ///     Verifies that negative acknowledgement with requeue delivers the message again.
    /// </summary>
    /// <returns>A task that completes when the requeue flow succeeds.</returns>
    [Fact]
    public async Task ConsumeAsync_NackWithRequeue_RedeliversMessage()
    {
        var queueName = CreateUniqueQueueName("nack");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {

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
                    await message.ReturnToQueueAsync(token).ConfigureAwait(true);
                    return;
                }

                secondDelivery.TrySetResult(message);
                await message.AcceptAsync(token).ConfigureAwait(true);
            }).ConfigureAwait(true);


        var body = Encoding.UTF8.GetBytes($"retry-{BrokerName}");

        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = body
            }).ConfigureAwait(true);


        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstMessage = await firstDelivery.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(true);
        firstMessage.Redelivered.Should().BeFalse();

        var secondMessage = await secondDelivery.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(true);
        secondMessage.Redelivered.Should().BeTrue();
        secondMessage.Body.ToArray().Should().BeEquivalentTo(body);
        }
        }
    }

    /// <summary>
    ///     Verifies that LiteBus header constants round-trip through publish and consume.
    /// </summary>
    /// <returns>A task that completes when header values are preserved.</returns>
    [Fact]
    public async Task PublishAsync_WithLiteBusHeaders_PreservesHeaderValues()
    {
        var queueName = CreateUniqueQueueName("headers");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {

        var received = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            }).ConfigureAwait(true);


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
            }).ConfigureAwait(true);


        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var message = await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(true);
        message.MessageId.Should().Be(messageId);
        message.CorrelationId.Should().Be("corr-1");
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.ContractName).Should().Be("orders.order-submitted");
        AmqpHeaderValues.GetInt32(message.Headers, AmqpHeaders.ContractVersion).Should().Be(1);
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.CausationId).Should().Be("cause-1");
        AmqpHeaderValues.GetString(message.Headers, AmqpHeaders.TenantId).Should().Be("tenant-a");
        await message.AcceptAsync(CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Verifies that stopping the consumer prevents further handler invocations while leaving messages in the queue.
    /// </summary>
    /// <returns>A task that completes when stop cancels the subscription.</returns>
    [Fact]
    public async Task StopAsync_AfterStart_PreventsFurtherDeliveries()
    {
        var queueName = CreateUniqueQueueName("stop");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {

        var firstDelivery = new TaskCompletionSource<AmqpReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            async (message, token) =>
            {
                firstDelivery.TrySetResult(message);
                await message.AcceptAsync(token).ConfigureAwait(true);
            }).ConfigureAwait(true);


        var firstBody = Encoding.UTF8.GetBytes($"first-{BrokerName}");

        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = firstBody
            }).ConfigureAwait(true);


        using var firstWait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await firstDelivery.Task.WaitAsync(firstWait.Token).ConfigureAwait(true);

        await consumer.StopAsync(CancellationToken.None).ConfigureAwait(true);

        var secondBody = Encoding.UTF8.GetBytes($"second-{BrokerName}");

        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = secondBody
            }).ConfigureAwait(true);


        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);

         var verifyChannel = await manager.CreateChannelAsync().ConfigureAwait(true);
         await using (verifyChannel.ConfigureAwait(false))
         {
        var queued = await verifyChannel.BasicGetAsync(queueName, false).ConfigureAwait(false);
        queued.Should().NotBeNull();
        queued!.Body.ToArray().Should().BeEquivalentTo(secondBody);
        await verifyChannel.BasicAckAsync(queued.DeliveryTag, false).ConfigureAwait(false);
        }
        }
        }
    }

    /// <summary>
    ///     Verifies that publish honours cancellation before the broker accepts the message.
    /// </summary>
    /// <returns>A task that completes when cancellation is observed.</returns>
    [Fact]
    public async Task PublishAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var act = () => publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = CreateUniqueQueueName("cancel-publish"),
                Body = Encoding.UTF8.GetBytes("cancelled")
            },
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    /// <summary>
    ///     Verifies that starting a consumer twice fails fast.
    /// </summary>
    /// <returns>A task that completes when the duplicate start is rejected.</returns>
    [Fact]
    public async Task StartAsync_WhenAlreadyStarted_ThrowsLiteBusConfigurationException()
    {
        var queueName = CreateUniqueQueueName("double-start");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
         var consumer = new AmqpConsumer(manager);
         await using (consumer.ConfigureAwait(false))
         {

        await consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (_, _) => Task.CompletedTask).ConfigureAwait(true);


        var act = () => consumer.StartAsync(
            new AmqpConsumerOptions { QueueName = queueName },
            (_, _) => Task.CompletedTask);

        await act.Should().ThrowAsync<AmqpTransportConfigurationException>()
            .WithMessage("*already started*");

        await consumer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    /// <summary>
    ///     Verifies that connection creation fails when the broker endpoint is unreachable.
    /// </summary>
    /// <returns>A task that completes when the connection attempt fails.</returns>
    [Fact]
    public async Task GetConnectionAsync_WithUnreachableBroker_Throws()
    {
        var unreachable = CreateUnreachableConnectionOptions();
         var manager = new AmqpConnectionManager(unreachable);
         await using (manager.ConfigureAwait(false))
         {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var act = () => manager.GetConnectionAsync(timeout.Token);

        await act.Should().ThrowAsync<Exception>();
        }
    }

    /// <summary>
    ///     Creates connection options that target a closed port on the same host as the fixture broker.
    /// </summary>
    /// <returns>Connection options that cannot reach an AMQP listener.</returns>
    private AmqpConnectionOptions CreateUnreachableConnectionOptions()
    {
        if (ConnectionOptions.Uri is not null)
        {
            var builder = new UriBuilder(ConnectionOptions.Uri) { Port = 1 };

            return new AmqpConnectionOptions
            {
                Uri = builder.Uri,
                AutomaticRecoveryEnabled = false,
                ClientProvidedName = "LiteBus.Transport.IntegrationTests.Amqp.Unreachable"
            };
        }

        return new AmqpConnectionOptions
        {
            HostName = ConnectionOptions.HostName,
            Port = 1,
            UserName = ConnectionOptions.UserName,
            Password = ConnectionOptions.Password,
            VirtualHost = ConnectionOptions.VirtualHost,
            AutomaticRecoveryEnabled = false,
            ClientProvidedName = "LiteBus.Transport.IntegrationTests.Amqp.Unreachable"
        };
    }

    /// <summary>
    ///     Verifies that publish recreates a working channel after the shared connection closes.
    /// </summary>
    /// <returns>A task that completes when publish succeeds on a new channel.</returns>
    [Fact]
    public async Task PublishAsync_AfterConnectionClosed_RecreatesPublishChannel()
    {
        var queueName = CreateUniqueQueueName("publish-recovery");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var publisher = new AmqpPublisher(manager, new TransportCircuitBreakerRegistry());

        var setupChannel = await manager.CreateChannelAsync().ConfigureAwait(true);
        await using (setupChannel.ConfigureAwait(true))
        {
            await setupChannel.QueueDeclareAsync(queueName, true, false, false).ConfigureAwait(true);
        }

        var connection = await manager.GetConnectionAsync().ConfigureAwait(true);

        await connection.CloseAsync(
            Constants.ReplySuccess,
            "integration-test",
            TimeSpan.FromSeconds(10),
            false,
            CancellationToken.None).ConfigureAwait(true);


        var body = Encoding.UTF8.GetBytes($"publish-recovery-{BrokerName}");

        await publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = queueName,
                Body = body
            }).ConfigureAwait(true);


         var channel = await manager.CreateChannelAsync().ConfigureAwait(false);
         await using (channel.ConfigureAwait(false))
         {
        var delivery = await channel.BasicGetAsync(queueName, false).ConfigureAwait(false);
        delivery.Should().NotBeNull();
        delivery!.Body.ToArray().Should().BeEquivalentTo(body);
        await channel.BasicAckAsync(delivery.DeliveryTag, false).ConfigureAwait(true);
        }
        }
    }

    /// <summary>
    ///     Verifies that the connection manager recreates a working connection after the previous one closes.
    /// </summary>
    /// <returns>A task that completes when publish succeeds on the recreated connection.</returns>
    [Fact]
    public async Task GetConnectionAsync_AfterConnectionClosed_RecreatesWorkingConnection()
    {
        var queueName = CreateUniqueQueueName("recovery");
         var manager = new AmqpConnectionManager(ConnectionOptions);
         await using (manager.ConfigureAwait(false))
         {
        var connection = await manager.GetConnectionAsync().ConfigureAwait(true);

        await connection.CloseAsync(
            Constants.ReplySuccess,
            "integration-test",
            TimeSpan.FromSeconds(10),
            false,
            CancellationToken.None).ConfigureAwait(true);


        var recreated = await manager.GetConnectionAsync().ConfigureAwait(true);
        recreated.IsOpen.Should().BeTrue();

         var channel = await manager.CreateChannelAsync().ConfigureAwait(true);
         await using (channel.ConfigureAwait(false))
         {
        await channel.QueueDeclareAsync(queueName, true, false, false).ConfigureAwait(true);

        var body = Encoding.UTF8.GetBytes("recovery");

        await channel.BasicPublishAsync(
            string.Empty,
            queueName,
            false,
            new BasicProperties(),
            body).ConfigureAwait(true);


        var delivery = await channel.BasicGetAsync(queueName, false).ConfigureAwait(false);
        delivery.Should().NotBeNull();
        delivery!.Body.ToArray().Should().BeEquivalentTo(body);
        await channel.BasicAckAsync(delivery.DeliveryTag, false).ConfigureAwait(false);
        }
        }
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
