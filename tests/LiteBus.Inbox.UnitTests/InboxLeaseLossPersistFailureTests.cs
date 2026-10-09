using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.DurableMessaging.Abstractions.Processing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies a failed lease-loss persist is recorded as an outcome instead of aborting the whole inbox pass.
/// </summary>
public sealed class InboxLeaseLossPersistFailureTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a state writer that throws on the first lease-loss persist still returns the other two results.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_first_lease_loss_persist_throws_should_return_other_results()
    {
        var store = new LeaseLossThrowingPersistInboxStore(new InMemoryInboxStore());
        var dispatchCount = 0;

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CountingInboxDispatcher(() => Interlocked.Increment(ref dispatchCount)),
            new InboxProcessorOptions
            {
                BatchSize = 3,
                LeaseOwner = "lease-loss-persist-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        for (var i = 0; i < 3; i++)
        {
            await store.Inner.AddAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "orders.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = BaseTime,
                AttemptCount = 0,
                Status = InboxStatus.Pending
            }).ConfigureAwait(false);
        }

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.LeasedCount.Should().Be(3);
        result.FailedCount.Should().Be(2);
        result.SucceededCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(0);
        dispatchCount.Should().Be(0);
    }

    /// <summary>
    ///     Counts dispatch invocations without performing any work.
    /// </summary>
    private sealed class CountingInboxDispatcher : IInboxDispatcher
    {
        private readonly Action _onDispatch;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CountingInboxDispatcher" /> class.
        /// </summary>
        /// <param name="onDispatch">The callback invoked on every dispatch.</param>
        public CountingInboxDispatcher(Action onDispatch)
        {
            _onDispatch = onDispatch;
        }

        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _onDispatch();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Fails every lease renewal and throws on the first lease-loss persist.
    /// </summary>
    private sealed class LeaseLossThrowingPersistInboxStore : IInboxProcessingStore
    {
        private int _persistAttempts;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LeaseLossThrowingPersistInboxStore" /> class.
        /// </summary>
        /// <param name="inner">The underlying in-memory store.</param>
        public LeaseLossThrowingPersistInboxStore(InMemoryInboxStore inner)
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
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _persistAttempts) == 1)
            {
                throw new InvalidOperationException("Simulated lease-loss persist failure.");
            }

            return Inner.PersistAsync(envelopes, cancellationToken);
        }
    }
}
