using System.Reflection;
using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Provides a broker-free AMQP connection manager that captures the consumer subscription for delivery simulation.
/// </summary>
public sealed class AmqpConsumerTestHarness : IAmqpConnectionManager
{
    /// <summary>
    ///     Gets the most recently created channel double.
    /// </summary>
    public FakeAmqpChannel? ActiveChannel { get; private set; }

    /// <summary>
    ///     Gets the consumer captured by the latest channel, if the consumer subscribed.
    /// </summary>
    public IAsyncBasicConsumer? CapturedConsumer => ActiveChannel?.CapturedConsumer;

    /// <inheritdoc />
    public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Consumer tests use channels only.");
    }

    /// <inheritdoc />
    public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var channel = DispatchProxy.Create<IChannel, FakeAmqpChannel>();
        var fake = (FakeAmqpChannel) (object) channel;
        ActiveChannel = fake;
        return Task.FromResult(channel);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Simulates one broker delivery on the captured consumer subscription.
    /// </summary>
    /// <param name="deliveryTag">The broker delivery tag assigned to the simulated delivery.</param>
    /// <param name="cancellationToken">The token used to cancel the simulated delivery.</param>
    /// <returns>A task that completes when the simulated delivery has been dispatched.</returns>
    public Task DeliverAsync(ulong deliveryTag, CancellationToken cancellationToken = default)
    {
        var consumer = CapturedConsumer;

        if (consumer is null)
        {
            throw new InvalidOperationException("Start the consumer before simulating a delivery.");
        }

        var properties = DispatchProxy.Create<IReadOnlyBasicProperties, FakeBasicProperties>();

        return consumer.HandleBasicDeliverAsync(
            "test-tag",
            deliveryTag,
            false,
            string.Empty,
            "route",
            properties,
            new ReadOnlyMemory<byte>([1, 2, 3]),
            cancellationToken);
    }

    /// <summary>
    ///     A channel stand-in that captures the consumer and records acknowledgement calls.
    /// </summary>
    // DispatchProxy.Create needs an unsealed proxy type.
    public class FakeAmqpChannel : DispatchProxy
    {
        /// <summary>
        ///     Gets the consumer supplied to BasicConsumeAsync.
        /// </summary>
        public IAsyncBasicConsumer? CapturedConsumer { get; private set; }

        /// <summary>
        ///     Gets the number of BasicAckAsync calls observed on this channel.
        /// </summary>
        public int AckCount { get; private set; }

        /// <summary>
        ///     Gets the number of BasicNackAsync calls observed on this channel.
        /// </summary>
        public int NackCount { get; private set; }

        /// <summary>
        ///     Intercepts channel calls made during consumer start, delivery, and stop.
        /// </summary>
        /// <param name="targetMethod">The intercepted channel method.</param>
        /// <param name="args">The arguments passed to the channel method.</param>
        /// <returns>The stand-in result for the intercepted call.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case "BasicConsumeAsync":
                    if (args is null || args.Length < 7 || args[6] is not IAsyncBasicConsumer captured)
                    {
                        throw new InvalidOperationException("BasicConsumeAsync was called without a consumer.");
                    }

                    CapturedConsumer = captured;
                    return Task.FromResult("test-tag");
                case "BasicAckAsync":
                    AckCount++;
                    return CompletedFor(targetMethod.ReturnType);
                case "BasicNackAsync":
                    NackCount++;
                    return CompletedFor(targetMethod.ReturnType);
                case "DisposeAsync":
                    return default(ValueTask);
                case "Dispose":
                    return null;
                case "get_IsOpen":
                    return true;
                default:
                    return DefaultFor(targetMethod.ReturnType);
            }
        }
    }

    /// <summary>
    ///     A message-properties stand-in with default values for simulated deliveries.
    /// </summary>
    // DispatchProxy.Create needs an unsealed proxy type.
    public class FakeBasicProperties : DispatchProxy
    {
        /// <summary>
        ///     Intercepts property reads on the simulated delivery properties.
        /// </summary>
        /// <param name="targetMethod">The intercepted property accessor.</param>
        /// <param name="args">The arguments passed to the property accessor.</param>
        /// <returns>The default value for the intercepted member.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return DefaultFor(targetMethod.ReturnType);
        }
    }

    /// <summary>
    ///     Returns a completed task in the shape the proxied method declares, <see cref="ValueTask" /> or <see cref="Task" />.
    /// </summary>
    /// <param name="returnType">The return type of the proxied method.</param>
    /// <returns>A completed task of that shape.</returns>
    private static object CompletedFor(Type returnType)
    {
        return returnType == typeof(ValueTask) ? default(ValueTask) : Task.CompletedTask;
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
