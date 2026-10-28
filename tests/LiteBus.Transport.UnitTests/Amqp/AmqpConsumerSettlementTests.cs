using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies an AMQP delivery settled by its handler is never settled a second time.
/// </summary>
public sealed class AmqpConsumerSettlementTests
{
    /// <summary>
    ///     Verifies a native handler that accepts and then throws does not trigger the failure-path requeue.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task NativeHandlerThatSettlesThenThrows_ShouldNotNackAgain()
    {
        var harness = new AmqpConsumerTestHarness();
        var consumer = new AmqpConsumer(harness);
        await using (consumer.ConfigureAwait(false))
        {
            var handlerRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                async (message, token) =>
                {
                    await message.AcceptAsync(token).ConfigureAwait(false);
                    handlerRan.TrySetResult();
                    throw new InvalidOperationException("late failure");
                }).ConfigureAwait(false);

            await harness.DeliverAsync(42).ConfigureAwait(false);
            await handlerRan.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

            harness.ActiveChannel!.AckCount.Should().Be(1);
            harness.ActiveChannel.NackCount.Should().Be(0);

            await consumer.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies a neutral handler that accepts and then throws does not trigger the invoker requeue.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task NeutralHandlerThatSettlesThenThrows_ShouldNotReturnToQueueAgain()
    {
        var harness = new AmqpConsumerTestHarness();
        var consumer = new AmqpConsumer(harness);
        await using (consumer.ConfigureAwait(false))
        {
            IMessageConsumer neutral = consumer;
            var handlerRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await neutral.StartAsync(
                new TransportConsumerOptions { Destination = "orders" },
                async (message, token) =>
                {
                    await message.AcceptAsync(token).ConfigureAwait(false);
                    handlerRan.TrySetResult();
                    throw new InvalidOperationException("late failure");
                }).ConfigureAwait(false);

            await harness.DeliverAsync(42).ConfigureAwait(false);
            await handlerRan.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

            harness.ActiveChannel!.AckCount.Should().Be(1);
            harness.ActiveChannel.NackCount.Should().Be(0);

            await consumer.StopAsync().ConfigureAwait(false);
        }
    }
}
