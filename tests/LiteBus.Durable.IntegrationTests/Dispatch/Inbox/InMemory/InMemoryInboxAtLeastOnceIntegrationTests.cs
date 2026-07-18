using LiteBus.Transport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Testing;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Inbox.InMemory;

/// <summary>
///     Verifies inbox at-least-once dispatch when terminal persist fails after a successful handler dispatch.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryInboxAtLeastOnceIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a simulated crash after dispatch causes a second handler invocation on lease reclaim.
    /// </summary>
    /// <returns>A task that completes when duplicate dispatch is observed.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var innerStore = new InMemoryInboxStore(timeProvider: clock);
        var store = new SkippingCompletedPersistInboxStore(innerStore);
        var dispatchCount = 0;
        var secondDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dispatcher = new CountingInboxDispatcher(() =>
        {
            if (Interlocked.Increment(ref dispatchCount) == 2)
            {
                secondDispatch.TrySetResult();
            }
        });

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "at-least-once-worker",
                LeaseDuration = TimeSpan.FromSeconds(5),
                LeaseHeartbeatInterval = TimeSpan.Zero,
                Retry = new RetryOptions { UseJitter = false }
            },
            clock,
            []);

        var messageId = Guid.NewGuid();

        await innerStore.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = clock.GetUtcNow(),
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        dispatchCount.Should().Be(1);
        innerStore.Get(messageId).Status.Should().Be(InboxStatus.Processing);

        clock.Advance(TimeSpan.FromSeconds(6));
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await secondDispatch.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);

        dispatchCount.Should().Be(2);
        innerStore.Get(messageId).Status.Should().Be(InboxStatus.Completed);
        innerStore.Get(messageId).AttemptCount.Should().Be(2);
    }

    /// <summary>
    ///     Counts inbox dispatch invocations for at-least-once verification.
    /// </summary>
    private sealed class CountingInboxDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     The callback invoked on each dispatch attempt.
        /// </summary>
        private readonly Action _onDispatch;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CountingInboxDispatcher" /> class.
        /// </summary>
        /// <param name="onDispatch">The callback invoked on each dispatch attempt.</param>
        public CountingInboxDispatcher(Action onDispatch)
        {
            _onDispatch = onDispatch;
        }

        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _onDispatch();
            return Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>
    ///     Skips the first terminal persist attempt for completed envelopes to simulate a crash after dispatch.
    /// </summary>
    private sealed class SkippingCompletedPersistInboxStore : IInboxProcessingStore
    {
        /// <summary>
        ///     The number of completed persist attempts observed by this wrapper.
        /// </summary>
        private int _completedPersistAttempts;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SkippingCompletedPersistInboxStore" /> class.
        /// </summary>
        /// <param name="inner">The underlying in-memory store.</param>
        public SkippingCompletedPersistInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        /// <summary>
        ///     Gets the underlying in-memory store.
        /// </summary>
        public InMemoryInboxStore Inner { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            if (envelopes.Any(envelope => envelope.Status == InboxStatus.Completed) &&
                Interlocked.Increment(ref _completedPersistAttempts) == 1)
            {
                return Task.FromResult(PersistResult.FromOutcome(0, envelopes.Count));
            }

            return Inner.PersistAsync(envelopes, cancellationToken);
        }
    }
}
