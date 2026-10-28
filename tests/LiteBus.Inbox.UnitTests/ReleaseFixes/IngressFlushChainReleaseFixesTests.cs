using System.Reflection;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies timer flushes replace a completed aggregate instead of chaining it forever.
/// </summary>
public sealed class IngressFlushChainReleaseFixesTests
{
    /// <summary>
    ///     Verifies a new timer flush does not chain onto a completed previous flush.
    /// </summary>
    /// <returns>A task that completes when the chain assertion succeeds.</returns>
    [Fact]
    public async Task TimerFlush_WhenPreviousFlushCompleted_ShouldReplaceAggregate()
    {
        var inbox = new GatedBatchInbox();
        var consumer = IngressConsumerReleaseFixesSupport.CreateConsumer(
            inbox,
            new TransportInboxIngressOptions
            {
                Safety = new TransportInboxIngressSafetyOptions
                {
                    BatchSize = 10,
                    EnableBatchAccept = true,
                    RequireStableIdentity = false,
                    BatchMaxWait = TimeSpan.FromMilliseconds(50)
                }
            });

        await IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                """{"value":1}"""u8.ToArray(),
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask)).ConfigureAwait(false);

        await WaitForConditionAsync(() => inbox.BatchAcceptCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var firstFlush = ReadActiveFlushTask(consumer);

        await IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                """{"value":2}"""u8.ToArray(),
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask)).ConfigureAwait(false);

        await WaitForConditionAsync(
            () => !ReferenceEquals(ReadActiveFlushTask(consumer), firstFlush),
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // A completed predecessor is replaced, not chained, so the active flush is never a Task.WhenAll node.
        ReadActiveFlushTask(consumer)!.GetType().Name.Should().NotContain("WhenAll");

        inbox.ReleaseSecondFlush();
        await WaitForConditionAsync(() => inbox.BatchAcceptCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        consumer.Dispose();
    }

    /// <summary>
    ///     Reads the active timer flush task tracked by the consumer.
    /// </summary>
    /// <param name="consumer">The ingress consumer under test.</param>
    /// <returns>The active flush task, or <see langword="null" /> when no flush started yet.</returns>
    private static Task? ReadActiveFlushTask(TransportInboxIngressConsumer consumer)
    {
        var field = typeof(TransportInboxIngressConsumer).GetField(
                        "_activeBatchFlushTask",
                        BindingFlags.NonPublic | BindingFlags.Instance) ??
                    throw new InvalidOperationException("_activeBatchFlushTask not found.");

        return (Task?) field.GetValue(consumer);
    }

    /// <summary>
    ///     Waits until a condition holds or a timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to poll.</param>
    /// <param name="timeout">The maximum wait.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None).ConfigureAwait(false);
        }

        condition().Should().BeTrue();
    }

    /// <summary>
    ///     Inbox test double that blocks the second batch accept until released.
    /// </summary>
    private sealed class GatedBatchInbox : IInbox
    {
        /// <summary>
        ///     Gets the gate that blocks the second batch accept call.
        /// </summary>
        private readonly TaskCompletionSource _secondFlushGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Gets the backing count of batch accept calls observed.
        /// </summary>
        private int _batchAcceptCount;

        /// <summary>
        ///     Gets the number of batch accept calls observed.
        /// </summary>
        public int BatchAcceptCount => Volatile.Read(ref _batchAcceptCount);

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            InboxAcceptItem<TMessage> item,
            CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<InboxReceipt> AcceptAsync(
            InboxAcceptItem item,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<InboxAcceptItem> items,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _batchAcceptCount) == 2)
            {
                await _secondFlushGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return [];
        }

        /// <summary>
        ///     Releases the blocked second batch accept call.
        /// </summary>
        public void ReleaseSecondFlush()
        {
            _secondFlushGate.TrySetResult();
        }
    }
}
