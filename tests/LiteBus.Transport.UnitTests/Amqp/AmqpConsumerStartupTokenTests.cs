using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies AMQP deliveries survive cancellation and disposal of the startup token.
/// </summary>
public sealed class AmqpConsumerStartupTokenTests
{
    /// <summary>
    ///     Verifies cancelling and disposing the startup token after startup does not affect deliveries.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StartAsync_AfterStartupTokenCancelledAndDisposed_ShouldStillDeliver()
    {
        var harness = new AmqpConsumerTestHarness();
        var consumer = new AmqpConsumer(harness);
        await using (consumer.ConfigureAwait(false))
        {
            using var startupSource = new CancellationTokenSource();
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                (message, token) =>
                {
                    delivered.TrySetResult();
                    return message.AcceptAsync(token);
                },
                startupSource.Token).ConfigureAwait(false);

            startupSource.Cancel();
            startupSource.Dispose();

            await harness.DeliverAsync(1).ConfigureAwait(false);
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            harness.ActiveChannel!.AckCount.Should().Be(1);

            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.WaitUntilStoppedAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies the consumer restarts cleanly after the subscription-owned token is stopped and disposed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StopAsync_ThenStartAsync_ShouldDeliverOnNewSubscription()
    {
        var harness = new AmqpConsumerTestHarness();
        var consumer = new AmqpConsumer(harness);
        await using (consumer.ConfigureAwait(false))
        {
            await consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                static (message, token) => message.AcceptAsync(token)).ConfigureAwait(false);
            await consumer.StopAsync().ConfigureAwait(false);

            var redelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                (message, token) =>
                {
                    redelivered.TrySetResult();
                    return message.AcceptAsync(token);
                }).ConfigureAwait(false);

            await harness.DeliverAsync(2).ConfigureAwait(false);
            await redelivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            harness.ActiveChannel!.AckCount.Should().Be(1);

            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.WaitUntilStoppedAsync().ConfigureAwait(false);
        }
    }
}
