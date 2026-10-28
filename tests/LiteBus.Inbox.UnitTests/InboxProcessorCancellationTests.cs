using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies a cancelled inbox dispatch leaves the envelope untouched: no outcome recorded, no attempt consumed
///     beyond the lease, no error recorded, and no visibility moved.
/// </summary>
public sealed class InboxProcessorCancellationTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a dispatch cancelled by the caller produces no terminal outcome.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_when_caller_cancels_should_leave_envelope_without_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        var updated = await InboxProcessorEnvelopeHandler.ProcessAsync(
            CreateLeasedEnvelope(),
            new CancellingInboxDispatcher(cancellation, wrapCancellation: false),
            new InboxProcessorOptions { Retry = new RetryOptions { UseJitter = false } },
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            [],
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
        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        var updated = await InboxProcessorEnvelopeHandler.ProcessAsync(
            CreateLeasedEnvelope(),
            new CancellingInboxDispatcher(cancellation, wrapCancellation: true),
            new InboxProcessorOptions { Retry = new RetryOptions { UseJitter = false } },
            TimeProvider.System,
            accumulator,
            NullLogger.Instance,
            [],
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
        var store = new InMemoryInboxStore();
        using var cancellation = new CancellationTokenSource();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CancellingInboxDispatcher(cancellation, wrapCancellation: false),
            new InboxProcessorOptions
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

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var stored = store.Get(messageId);

        // The processor releases the lease of a dispatch it cancelled, refunding the attempt the lease claimed.
        stored.AttemptCount.Should().Be(0);
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.Status.Should().Be(InboxStatus.Pending);
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
        var store = new InMemoryInboxStore();
        using var cancellation = new CancellationTokenSource();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new CancellingInboxDispatcher(cancellation, wrapCancellation: true),
            new InboxProcessorOptions
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

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var act = () => processor.ProcessPendingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var stored = store.Get(messageId);

        // The processor releases the lease of a dispatch it cancelled, refunding the attempt the lease claimed.
        stored.AttemptCount.Should().Be(0);
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.Status.Should().Be(InboxStatus.Pending);
        stored.LastError.Should().BeNull();
        stored.ErrorType.Should().BeNull();
        stored.FirstFailedAt.Should().BeNull();
        stored.VisibleAfter.Should().BeNull();
    }

    /// <summary>
    ///     Verifies a handler-internal timeout takes the normal failure path with an error type instead of lease loss.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_handler_cancels_own_token_should_record_failure_with_error_type()
    {
        var store = new InMemoryInboxStore();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new TimeoutInboxDispatcher(),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "timeout-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { MaxAttempts = 5, UseJitter = false }
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync(CancellationToken.None).ConfigureAwait(false);

        var stored = store.Get(messageId);
        stored.Status.Should().Be(InboxStatus.Failed);
        stored.ErrorType.Should().Be(typeof(TaskCanceledException).FullName);
        stored.LastError.Should().NotBeNullOrWhiteSpace();
        stored.VisibleAfter.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies a handler-internal timeout dead-letters after exhausting attempts instead of retrying forever.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_handler_timeout_exhausts_attempts_should_dead_letter()
    {
        var store = new InMemoryInboxStore();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new TimeoutInboxDispatcher(),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "timeout-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { MaxAttempts = 1, UseJitter = false }
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync(CancellationToken.None).ConfigureAwait(false);

        var stored = store.Get(messageId);
        stored.Status.Should().Be(InboxStatus.DeadLettered);
        stored.ErrorType.Should().Be(typeof(TaskCanceledException).FullName);
    }

    private static InboxEnvelope CreateLeasedEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 1,
            Status = InboxStatus.Processing
        };
    }

    private sealed class TimeoutInboxDispatcher : IInboxDispatcher
    {
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("Handler operation timed out.");
        }
    }

    private sealed class CancellingInboxDispatcher : IInboxDispatcher
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _wrapCancellation;

        public CancellingInboxDispatcher(CancellationTokenSource cancellation, bool wrapCancellation)
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            _cancellation = cancellation;
            _wrapCancellation = wrapCancellation;
        }

        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();

            throw _wrapCancellation
                ? new InboxDispatchException("third-party dispatch wrapper", new OperationCanceledException())
                : new OperationCanceledException(_cancellation.Token);
        }
    }
}
