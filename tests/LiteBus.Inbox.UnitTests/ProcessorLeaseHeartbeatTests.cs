using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies lease heartbeat behavior through the pipelined inbox processor.
/// </summary>
public sealed class ProcessorLeaseHeartbeatTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 12, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the first failed renewal cancels dispatch and persists a retryable failed outcome.
    /// </summary>
    /// <returns>A task that completes when the lease-loss assertion succeeds.</returns>
    [Fact]
    public async Task RunWithHeartbeat_when_renewal_fails_should_persist_failed_outcome()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = new LeaseFailingInboxStore(new InMemoryInboxStore(timeProvider: clock));
        var commandId = Guid.NewGuid();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new DelayingInboxDispatcher(),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "heartbeat-worker",
                LeaseDuration = TimeSpan.FromSeconds(10),
                LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(50),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        await store.Inner.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var stored = store.Inner.Get(commandId);
        stored.Status.Should().Be(InboxStatus.Failed);
        stored.LeaseOwner.Should().BeNull();
        stored.LastError.Should().Be(MessageProcessorDiagnostics.LeaseLostDuringProcessingError);
    }

    /// <summary>
    ///     Dispatcher that runs long enough for lease renewal to be attempted.
    /// </summary>
    private sealed class DelayingInboxDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>
    ///     Lease store that succeeds on the first renewal and fails on subsequent attempts.
    /// </summary>
    private sealed class LeaseFailingInboxStore : IInboxProcessingStore
    {
        private int _renewalAttempts;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LeaseFailingInboxStore" /> class.
        /// </summary>
        /// <param name="inner">The inner store that owns envelope persistence.</param>
        public LeaseFailingInboxStore(InMemoryInboxStore inner)
        {
            Inner = inner;
        }

        /// <summary>
        ///     Gets the inner store that owns envelope persistence.
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
            _renewalAttempts++;
            return Task.FromResult(_renewalAttempts == 1);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            return Inner.PersistAsync(envelopes, cancellationToken);
        }
    }
}
