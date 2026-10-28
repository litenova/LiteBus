using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies shutdown cancellation aborts batch fallback instead of attempting cancelled deliveries.
/// </summary>
public sealed class IngressBatchFallbackReleaseFixesTests
{
    /// <summary>
    ///     Verifies cancellation during batch fallback aborts the remaining deliveries and restores admission.
    /// </summary>
    /// <returns>A task that completes when the cancellation assertion succeeds.</returns>
    [Fact]
    public async Task HandleDeliveryAsync_WhenCancelledDuringBatchFallback_ShouldAbortAndRestoreAdmission()
    {
        var inbox = new BatchFailingInbox();
        var consumer = IngressConsumerReleaseFixesSupport.CreateConsumer(
            inbox,
            new TransportInboxIngressOptions
            {
                Safety = new TransportInboxIngressSafetyOptions
                {
                    BatchSize = 2,
                    EnableBatchAccept = true,
                    RequireStableIdentity = false,
                    BatchMaxWait = TimeSpan.FromSeconds(30)
                }
            });

        await IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                """{"value":1}"""u8.ToArray(),
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask)).ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource();
        var flush = IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                """{"value":2}"""u8.ToArray(),
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask),
            cancellationSource.Token);

        await WaitForConditionAsync(() => inbox.SingleAcceptCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var act = async () => await flush.ConfigureAwait(false);
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        inbox.SingleAcceptCount.Should().Be(1);

        await IngressConsumerReleaseFixesSupport.InvokeHandleDeliveryAsync(
            consumer,
            IngressConsumerReleaseFixesSupport.CreateMessage(
                """{"value":3}"""u8.ToArray(),
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        consumer.Dispose();
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
    ///     Inbox test double that fails batch accept and blocks single accept until cancellation.
    /// </summary>
    private sealed class BatchFailingInbox : IInbox
    {
        /// <summary>
        ///     Gets the backing count of single accept calls observed.
        /// </summary>
        private int _singleAcceptCount;

        /// <summary>
        ///     Gets the number of single accept calls observed.
        /// </summary>
        public int SingleAcceptCount => Volatile.Read(ref _singleAcceptCount);

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
            InboxAcceptItem<TMessage> item,
            CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public async Task<InboxReceipt> AcceptAsync(
            InboxAcceptItem item,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _singleAcceptCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable: the delay ends only through cancellation.");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
            IReadOnlyList<InboxAcceptItem> items,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<InboxReceipt>>(new IOException("batch store down"));
        }
    }
}
