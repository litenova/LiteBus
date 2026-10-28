using System.Text;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Verifies in-memory deliveries are redelivered when a handler returns without settling.
/// </summary>
public sealed class InMemoryUnsettledDeliveryTests
{
    /// <summary>
    ///     Verifies a delivery no handler settled is redelivered instead of dropped.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HandlerWithoutSettlement_ShouldRedeliverInsteadOfDrop()
    {
        var broker = new InMemoryTransportBroker(new InMemoryTransportOptions { DestinationCapacity = 2 });
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var deliveryCount = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "unsettled" },
            (message, cancellationToken) =>
            {
                deliveryCount++;

                if (deliveryCount >= 2)
                {
                    return message.AcceptAsync(cancellationToken);
                }

                return Task.CompletedTask;
            }).ConfigureAwait(false);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "unsettled",
            Body = Encoding.UTF8.GetBytes("payload")
        }).ConfigureAwait(false);

        await WaitForAsync(() => Volatile.Read(ref deliveryCount) >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Volatile.Read(ref deliveryCount).Should().BeGreaterThanOrEqualTo(2);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "unsettled",
            Body = Encoding.UTF8.GetBytes("second")
        }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        await WaitForAsync(() => Volatile.Read(ref deliveryCount) >= 3, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Volatile.Read(ref deliveryCount).Should().BeGreaterThanOrEqualTo(3);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long) timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
    }
}
