using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies a second AMQP consumer start throws the lifecycle exception promised by the consumer contract.
/// </summary>
public sealed class AmqpConsumerDoubleStartTests
{
    /// <summary>
    ///     Verifies a second start throws InvalidOperationException instead of a configuration exception.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task StartAsync_WhenAlreadyStarted_ShouldThrowInvalidOperationException()
    {
        var harness = new AmqpConsumerTestHarness();
        var consumer = new AmqpConsumer(harness);
        await using (consumer.ConfigureAwait(false))
        {
            static Task OnMessage(AmqpReceivedMessage message, CancellationToken token) => Task.CompletedTask;

            await consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                OnMessage).ConfigureAwait(false);

            var act = () => consumer.StartAsync(
                new AmqpConsumerOptions { QueueName = "orders" },
                OnMessage);

            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.WaitUntilStoppedAsync().ConfigureAwait(false);
        }
    }
}
