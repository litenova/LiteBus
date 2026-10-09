using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies AMQP consumer admission control on the typed subscription path.
/// </summary>
public sealed class AmqpConsumerBoundedHandlerTests
{
    /// <summary>
    ///     Verifies a non-positive limit fails before any broker connection is opened.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StartAsync_WithZeroMaxInFlight_ShouldThrow()
    {
        var consumer = new AmqpConsumer(new NeverConnectedConnectionManager());
        await using (consumer.ConfigureAwait(false))
        {
            var act = () => consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders", MaxInFlightMessages = 0 },
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies concurrent deliveries never exceed the configured admission limit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CreateBoundedAmqpHandler_WithMaxOne_ShouldSerializeHandlers()
    {
        var current = 0;
        var peak = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Handler(AmqpReceivedMessage message, CancellationToken token)
        {
            var now = Interlocked.Increment(ref current);
            Interlocked.Exchange(ref peak, Math.Max(peak, now));
            entered.TrySetResult();
            await Task.Delay(50, token).ConfigureAwait(false);
            Interlocked.Decrement(ref current);
        }

        var bounded = AmqpConsumer.CreateBoundedAmqpHandler(Handler, 1);
        var message = new AmqpReceivedMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = new Dictionary<string, object?>(),
            DeliveryTag = 1,
            AckDelegate = (_, _) => Task.CompletedTask,
            NackDelegate = (_, _, _) => Task.CompletedTask
        };

        var first = bounded(message, CancellationToken.None);
        await entered.Task.ConfigureAwait(false);
        var second = bounded(message, CancellationToken.None);

        await first.ConfigureAwait(false);
        await second.ConfigureAwait(false);
        peak.Should().Be(1);
    }

    private sealed class NeverConnectedConnectionManager : IAmqpConnectionManager
    {
        public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Validation tests must not open a broker connection.");
        }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Validation tests must not open a broker connection.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
