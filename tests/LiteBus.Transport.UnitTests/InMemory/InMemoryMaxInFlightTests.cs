using System.Text;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Verifies the in-memory consumer enforces its documented concurrency bound of one handler at a time.
/// </summary>
public sealed class InMemoryMaxInFlightTests
{
    /// <summary>
    ///     Verifies eight blocked deliveries with eight requested in-flight slots still peak at one concurrent handler.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithMaxInFlightEight_ShouldEnforceInMemoryBoundOfOne()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var activeHandlers = 0;
        var peakHandlers = 0;
        var enteredHandlers = 0;
        var completedHandlers = 0;
        var releaseHandlers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await consumer.StartAsync(
                new TransportConsumerOptions { Destination = "max-in-flight-bound", MaxInFlightMessages = 8 },
                async (message, cancellationToken) =>
                {
                    var current = Interlocked.Increment(ref activeHandlers);
                    UpdatePeak(ref peakHandlers, current);
                    Interlocked.Increment(ref enteredHandlers);

                    try
                    {
                        await releaseHandlers.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeHandlers);
                    }

                    Interlocked.Increment(ref completedHandlers);
                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            for (var i = 0; i < 8; i++)
            {
                await publisher.PublishAsync(new TransportPublishRequest
                {
                    Destination = "max-in-flight-bound",
                    Body = Encoding.UTF8.GetBytes("payload")
                }).ConfigureAwait(false);
            }

            await WaitForAsync(() => Volatile.Read(ref enteredHandlers) >= 1, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Volatile.Read(ref enteredHandlers).Should().BeGreaterThanOrEqualTo(1);

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            Volatile.Read(ref enteredHandlers).Should().Be(1);
            Volatile.Read(ref peakHandlers).Should().Be(1);

            releaseHandlers.TrySetResult();

            await WaitForAsync(() => Volatile.Read(ref completedHandlers) == 8, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Volatile.Read(ref completedHandlers).Should().Be(8);
            Volatile.Read(ref peakHandlers).Should().Be(1);
        }
        finally
        {
            releaseHandlers.TrySetResult();
            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.DisposeAsync().ConfigureAwait(false);
        }
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

    /// <summary>
    ///     Replaces a target with a larger candidate while preserving concurrent updates.
    /// </summary>
    /// <param name="target">The target updated atomically.</param>
    /// <param name="candidate">The candidate maximum.</param>
    private static void UpdatePeak(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);

        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);

            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
