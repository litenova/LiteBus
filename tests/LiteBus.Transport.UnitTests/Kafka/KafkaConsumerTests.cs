using System.Reflection;
using System.Text;
using Confluent.Kafka;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies Kafka consumer client ownership, transient-error recovery, seek retries, and stop semantics.
/// </summary>
public sealed class KafkaConsumerTests
{
    /// <summary>
    ///     Verifies the module registers the Confluent client as transient so each consumer owns one client.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterConsumerClientAsTransient()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" })
            .Build(configuration);

        var consumerClient = configuration.DependencyRegistry.Single(
            descriptor => descriptor.DependencyType == typeof(IConsumer<string, byte[]>));
        consumerClient.Lifetime.Should().Be(InstanceLifetime.Transient);
    }

    /// <summary>
    ///     Verifies disposing a consumer releases its owned client even when it never started.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DisposeAsync_WithoutStart_ShouldDisposeOwnedClient()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;

        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
        }

        fake.DisposeCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies stopping a consumer closes its owned client and disposing releases it.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StopAsync_AfterStart_ShouldCloseAndDisposeOwnedClient()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;
        fake.ConsumeFunc = _ =>
        {
            Thread.Sleep(10);
            return null;
        };

        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
            static Task OnMessage(TransportMessage message, CancellationToken token) => Task.CompletedTask;

            await consumer.StartAsync(new TransportConsumerOptions { Destination = "orders" }, OnMessage)
                .ConfigureAwait(false);
            await consumer.StopAsync().ConfigureAwait(false);

            fake.CloseCount.Should().Be(1);
        }

        fake.DisposeCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies a transient consume error backs off and keeps polling instead of faulting the loop.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task Consume_TransientError_ShouldBackOffAndContinue()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;

        var consumeCalls = 0;
        fake.ConsumeFunc = _ =>
        {
            var call = Interlocked.Increment(ref consumeCalls);

            if (call == 1)
            {
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]>(),
                    new Error(ErrorCode.Local_AllBrokersDown, "rolling restart", false));
            }

            if (call == 2)
            {
                return CreateRecord("orders", 0, 42);
            }

            Thread.Sleep(10);
            return null;
        };

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
            await consumer.StartAsync(
                new TransportConsumerOptions { Destination = "orders" },
                async (message, cancellationToken) =>
                {
                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    handled.TrySetResult();
                }).ConfigureAwait(false);

            await handled.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await consumer.StopAsync().ConfigureAwait(false);

            fake.CommitCount.Should().Be(1);
        }
    }

    /// <summary>
    ///     Verifies a fatal consume error faults the wait handle while stop completes without throwing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task Consume_FatalError_ShouldFaultWaitUntilStoppedAsyncAndNotStopAsync()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;
        fake.ConsumeFunc = _ => throw new ConsumeException(
            new ConsumeResult<byte[], byte[]>(),
            new Error(ErrorCode.Local_AllBrokersDown, "fatal client error", true));

        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
            static Task OnMessage(TransportMessage message, CancellationToken token) => Task.CompletedTask;

            await consumer.StartAsync(new TransportConsumerOptions { Destination = "orders" }, OnMessage)
                .ConfigureAwait(false);

            var waitBeforeStop = () => consumer.WaitUntilStoppedAsync();
            await waitBeforeStop.Should().ThrowAsync<ConsumeException>().ConfigureAwait(false);

            var stop = () => consumer.StopAsync();
            await stop.Should().NotThrowAsync().ConfigureAwait(false);

            var waitAfterStop = () => consumer.WaitUntilStoppedAsync();
            await waitAfterStop.Should().ThrowAsync<ConsumeException>().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies an unexpected loop failure surfaces through the wait handle instead of the stop call.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StopAsync_WhenLoopFaulted_ShouldRouteFaultToWaitUntilStoppedAsync()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;
        fake.ConsumeFunc = _ => throw new InvalidOperationException("Consume boom.");

        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
            static Task OnMessage(TransportMessage message, CancellationToken token) => Task.CompletedTask;

            await consumer.StartAsync(new TransportConsumerOptions { Destination = "orders" }, OnMessage)
                .ConfigureAwait(false);

            var waitBeforeStop = () => consumer.WaitUntilStoppedAsync();
            await waitBeforeStop.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            var stop = () => consumer.StopAsync();
            await stop.Should().NotThrowAsync().ConfigureAwait(false);

            var waitAfterStop = () => consumer.WaitUntilStoppedAsync();
            await waitAfterStop.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies a transient seek failure during requeue retries the rewind instead of killing the loop.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task Nack_RequeueWhenSeekFailsTransiently_ShouldRetrySeekAndContinue()
    {
        var client = DispatchProxy.Create<IConsumer<string, byte[]>, FakeKafkaConsumerProxy>();
        var fake = (FakeKafkaConsumerProxy) (object) client;

        var consumeCalls = 0;
        fake.ConsumeFunc = _ =>
        {
            var call = Interlocked.Increment(ref consumeCalls);

            if (call == 1)
            {
                return CreateRecord("orders", 0, 10);
            }

            if (call == 2)
            {
                return CreateRecord("orders", 0, 11);
            }

            Thread.Sleep(10);
            return null;
        };

        var seekCalls = 0;
        fake.SeekFunc = _ =>
        {
            if (Interlocked.Increment(ref seekCalls) == 1)
            {
                return new KafkaException(new Error(ErrorCode.Local_AllBrokersDown, "partition revoked", false));
            }

            return null;
        };

        var handlerCalls = 0;
        var secondHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new KafkaConsumer(client, new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
        await using (consumer.ConfigureAwait(false))
        {
            await consumer.StartAsync(
                new TransportConsumerOptions { Destination = "orders" },
                async (message, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref handlerCalls) == 1)
                    {
                        throw new InvalidOperationException("Handler boom.");
                    }

                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    secondHandled.TrySetResult();
                }).ConfigureAwait(false);

            await secondHandled.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await consumer.StopAsync().ConfigureAwait(false);

            var expectedOffset = new TopicPartitionOffset("orders", 0, 10);
            fake.SeekOffsets.Should().HaveCount(2);
            fake.SeekOffsets[0].Should().Be(expectedOffset);
            fake.SeekOffsets[1].Should().Be(expectedOffset);
            fake.CommitCount.Should().Be(1);
        }
    }

    private static ConsumeResult<string, byte[]> CreateRecord(string topic, int partition, int offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = "tenant-a",
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = new Headers()
            }
        };
    }

    /// <summary>
    ///     A Confluent consumer stand-in with scriptable consume, seek, commit, close, and dispose behavior.
    /// </summary>
    // DispatchProxy.Create needs an unsealed proxy type.
    public class FakeKafkaConsumerProxy : DispatchProxy
    {
        /// <summary>
        ///     Gets or sets the consume behavior invoked for each poll.
        /// </summary>
        public Func<TimeSpan, ConsumeResult<string, byte[]>?>? ConsumeFunc { get; set; }

        /// <summary>
        ///     Gets or sets the seek behavior returning the exception to throw, or <see langword="null" /> for success.
        /// </summary>
        public Func<TopicPartitionOffset, Exception?>? SeekFunc { get; set; }

        /// <summary>
        ///     Gets the topics the consumer subscribed to.
        /// </summary>
        public List<string> Subscriptions { get; } = [];

        /// <summary>
        ///     Gets the offsets the consumer seeked to, in call order.
        /// </summary>
        public List<TopicPartitionOffset> SeekOffsets { get; } = [];

        /// <summary>
        ///     Gets the number of commit calls observed.
        /// </summary>
        public int CommitCount { get; private set; }

        /// <summary>
        ///     Gets the number of close calls observed.
        /// </summary>
        public int CloseCount { get; private set; }

        /// <summary>
        ///     Gets the number of dispose calls observed.
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        ///     Intercepts consumer calls made during consume, settlement, and teardown.
        /// </summary>
        /// <param name="targetMethod">The intercepted consumer method.</param>
        /// <param name="args">The arguments passed to the consumer method.</param>
        /// <returns>The stand-in result for the intercepted call.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case "Consume" when args![0] is TimeSpan poll:
                    return ConsumeFunc?.Invoke(poll);
                case "Subscribe" when args![0] is string topic:
                    Subscriptions.Add(topic);
                    return null;
                case "Subscribe" when args![0] is IEnumerable<string> topics:
                    Subscriptions.AddRange(topics);
                    return null;
                case "Seek":
                    var offset = (TopicPartitionOffset) args![0]!;
                    SeekOffsets.Add(offset);
                    var failure = SeekFunc?.Invoke(offset);

                    if (failure is not null)
                    {
                        throw failure;
                    }

                    return null;
                case "Commit":
                    CommitCount++;
                    return DefaultFor(targetMethod.ReturnType);
                case "Close":
                    CloseCount++;
                    return null;
                case "Dispose":
                    DisposeCount++;
                    return null;
                default:
                    return DefaultFor(targetMethod.ReturnType);
            }
        }

        private static object? DefaultFor(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType == typeof(ValueTask))
            {
                return default(ValueTask);
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!;
                var generic = fromResult.MakeGenericMethod(resultType);
                return generic.Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                return Activator.CreateInstance(returnType);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
