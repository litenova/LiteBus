using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies provider-neutral transport consumer admission behavior.
/// </summary>
public sealed class TransportConsumerHandlerInvokerTests
{
    /// <summary>
    ///     Verifies a bounded handler never admits more than the configured number of concurrent callbacks.
    /// </summary>
    /// <returns>A task that completes when all callbacks leave the admission gate.</returns>
    [Fact]
    public async Task CreateBoundedHandler_WithThreeConcurrentCalls_ShouldAdmitConfiguredMaximum()
    {
        const int maxInFlightMessages = 2;
        var activeCalls = 0;
        var peakActiveCalls = 0;
        var enteredCalls = 0;
        var twoCallsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var boundedHandler = TransportConsumerHandlerInvoker.CreateBoundedHandler(
            async (_, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref activeCalls);
                InterlockedExtensions.Max(ref peakActiveCalls, active);

                if (Interlocked.Increment(ref enteredCalls) == maxInFlightMessages)
                {
                    twoCallsEntered.TrySetResult();
                }

                try
                {
                    await releaseCalls.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref activeCalls);
                }
            },
            maxInFlightMessages);

        var calls = Enumerable.Range(0, 3)
            .Select(_ => boundedHandler(CreateMessage(), CancellationToken.None))
            .ToArray();

        await twoCallsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Volatile.Read(ref enteredCalls).Should().Be(maxInFlightMessages);

        releaseCalls.TrySetResult();
        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        peakActiveCalls.Should().Be(maxInFlightMessages);
        enteredCalls.Should().Be(3);
    }

    /// <summary>
    ///     Creates a transport delivery that requires no broker settlement for a successful handler call.
    /// </summary>
    /// <returns>The transport delivery passed to the bounded handler.</returns>
    private static TransportMessage CreateMessage()
    {
        return new TransportMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = new Dictionary<string, object?>(),
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }

    /// <summary>
    ///     Provides atomic operations not exposed directly by <see cref="Interlocked" />.
    /// </summary>
    private static class InterlockedExtensions
    {
        /// <summary>
        ///     Replaces a target with a larger candidate while preserving concurrent updates.
        /// </summary>
        /// <param name="target">The target updated atomically.</param>
        /// <param name="candidate">The candidate maximum.</param>
        public static void Max(ref int target, int candidate)
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
}
