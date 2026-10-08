using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies a cancelled outbox dispatch leaves the envelope untouched: no outcome recorded, no attempt consumed
///     beyond the lease, no error recorded, and no visibility moved.
/// </summary>
public sealed class OutboxProcessorCancellationTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a dispatch cancelled by the caller produces no terminal outcome.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_when_caller_cancels_should_leave_envelope_without_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        var accumulator = new ProcessorPassAccumulator<OutboxEnvelope>();

        var updated = await OutboxProcessorEnvelopeHandler.ProcessAsync(
            CreateLeasedEnvelope(),
            new CancellingOutboxDispatcher(cancellation, wrapCancellation: false),
            new OutboxProcessorOptions { Retry = new RetryOptions { UseJitter = false } },
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            cancellation.Token).ConfigureAwait(false);

        updated.Should().BeNull();

        var result = accumulator.ToResult(1, TimeSpan.Zero);
        result.SucceededCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a cancellation wrapped in a dispatcher exception still produces no terminal outcome.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_when_wrapping_dispatcher_cancels_should_leave_envelope_without_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        var accumulator = new ProcessorPassAccumulator<OutboxEnvelope>();

        var updated = await OutboxProcessorEnvelopeHandler.ProcessAsync(
            CreateLeasedEnvelope(),
            new CancellingOutboxDispatcher(cancellation, wrapCancellation: true),
            new OutboxProcessorOptions { Retry = new RetryOptions { UseJitter = false } },
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            cancellation.Token).ConfigureAwait(false);

        updated.Should().BeNull();

        var result = accumulator.ToResult(1, TimeSpan.Zero);
        result.SucceededCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.DeadLetteredCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a pass cancelled mid-dispatch returns the message to the queue with its attempt refunded.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_dispatcher_cancels_pass_should_leave_stored_envelope_untouched()
    {
        var store = new InMemoryOutboxStore();
        using var cancellation = new CancellationTokenSource();

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CancellingOutboxDispatcher(cancellation, wrapCancellation: false),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "cancellation-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var stored = store.Get(messageId);

        // The processor releases the lease of a dispatch it cancelled, refunding the attempt the lease claimed.
        stored.AttemptCount.Should().Be(0);
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.Status.Should().Be(OutboxStatus.Pending);
        stored.LastError.Should().BeNull();
        stored.ErrorType.Should().BeNull();
        stored.FirstFailedAt.Should().BeNull();
        stored.VisibleAfter.Should().BeNull();
    }

    /// <summary>
    ///     Verifies a pass cancelled by a wrapping dispatcher returns the message to the queue with its attempt refunded.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_wrapping_dispatcher_cancels_pass_should_leave_stored_envelope_untouched()
    {
        var store = new InMemoryOutboxStore();
        using var cancellation = new CancellationTokenSource();

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new CancellingOutboxDispatcher(cancellation, wrapCancellation: true),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "cancellation-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var stored = store.Get(messageId);

        // The processor releases the lease of a dispatch it cancelled, refunding the attempt the lease claimed.
        stored.AttemptCount.Should().Be(0);
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.Status.Should().Be(OutboxStatus.Pending);
        stored.LastError.Should().BeNull();
        stored.ErrorType.Should().BeNull();
        stored.FirstFailedAt.Should().BeNull();
        stored.VisibleAfter.Should().BeNull();
    }

    private static OutboxEnvelope CreateLeasedEnvelope()
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 1,
            Status = OutboxStatus.Publishing
        };
    }

    private sealed class CancellingOutboxDispatcher : IOutboxDispatcher
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _wrapCancellation;

        public CancellingOutboxDispatcher(CancellationTokenSource cancellation, bool wrapCancellation)
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            _cancellation = cancellation;
            _wrapCancellation = wrapCancellation;
        }

        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();

            throw _wrapCancellation
                ? new LiteBusDispatchException("third-party dispatch wrapper", new OperationCanceledException())
                : new OperationCanceledException(_cancellation.Token);
        }
    }
}
