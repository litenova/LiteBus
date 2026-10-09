using System.Reflection;
using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies a failed AMQP consumer start releases its channel so a later start succeeds.
/// </summary>
public sealed class AmqpConsumerStartRecoveryTests
{
    /// <summary>
    ///     Verifies a start that fails in BasicConsumeAsync can be retried and stopped cleanly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StartAsync_AfterFailedStart_ShouldSucceedAndStop()
    {
        var connectionManager = new FlakyConnectionManager();
        var consumer = new AmqpConsumer(connectionManager);
        await using (consumer.ConfigureAwait(false))
        {
            var options = new AmqpConsumerOptions { QueueName = "orders" };
            static Task OnMessage(AmqpReceivedMessage message, CancellationToken token) => Task.CompletedTask;

            var first = () => consumer.StartAsync(options, OnMessage);
            await first.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            var failedStop = () => consumer.WaitUntilStoppedAsync();
            await failedStop.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            connectionManager.FailConsume = false;

            var second = () => consumer.StartAsync(options, OnMessage);
            await second.Should().NotThrowAsync().ConfigureAwait(false);

            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.WaitUntilStoppedAsync().ConfigureAwait(false);

            connectionManager.CreatedChannels.Should().Be(2);
            connectionManager.DisposedChannels.Should().Be(2);
        }
    }

    private sealed class FlakyConnectionManager : IAmqpConnectionManager
    {
        public bool FailConsume { get; set; } = true;

        public int CreatedChannels { get; private set; }

        public int DisposedChannels { get; private set; }

        public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Consumer start tests use channels only.");
        }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            CreatedChannels++;
            var channel = DispatchProxy.Create<IChannel, FakeChannelProxy>();
            var fake = (FakeChannelProxy)(object)channel;
            fake.FailConsume = FailConsume;
            fake.OnDisposed = () => DisposedChannels++;
            return Task.FromResult(channel);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     A channel stand-in that fails BasicConsumeAsync on demand and tracks disposal.
    /// </summary>
    // DispatchProxy.Create needs an unsealed proxy type.
    public class FakeChannelProxy : DispatchProxy
    {
        /// <summary>
        ///     Gets or sets a value indicating whether BasicConsumeAsync throws.
        /// </summary>
        public bool FailConsume { get; set; }

        /// <summary>
        ///     Gets or sets the callback invoked when the channel is disposed.
        /// </summary>
        public Action? OnDisposed { get; set; }

        /// <summary>
        ///     Intercepts channel calls made during consumer start and stop.
        /// </summary>
        /// <param name="targetMethod">The intercepted channel method.</param>
        /// <param name="args">The arguments passed to the channel method.</param>
        /// <returns>The stand-in result for the intercepted call.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case "BasicConsumeAsync" when FailConsume:
                    throw new InvalidOperationException("The broker rejected the consume request.");
                case "BasicConsumeAsync":
                    return DefaultFor(targetMethod.ReturnType);
                case "DisposeAsync":
                    OnDisposed?.Invoke();
                    return default(ValueTask);
                case "Dispose":
                    OnDisposed?.Invoke();
                    return null;
                case "get_IsOpen":
                    return true;
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
