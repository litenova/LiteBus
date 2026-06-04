using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default processor that leases inbox envelopes and dispatches them through <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     <para>
///         Each processing pass leases a bounded batch and calls <see cref="IInboxDispatcher.DispatchAsync" /> per
///         envelope. Deserialization and handler routing are the dispatcher's concern, not the processor's.
///     </para>
///     <para>
///         Failures from <see cref="IInboxDispatcher.DispatchAsync" /> are recorded through
///         <see cref="IInboxStateStore" />. Completion failures are not converted into retry state because dispatch
///         already succeeded; the active lease remains until it expires or a later completion attempt succeeds.
///         Cancellation is allowed to escape so the host can stop without converting shutdown into a failure record.
///     </para>
/// </remarks>
public sealed class InboxProcessor : Abstractions.IInboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to execute each leased envelope.
    /// </summary>
    private readonly IInboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the lease owner name assigned to envelopes claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly InboxProcessorOptions _options;

    /// <summary>
    ///     Gets the store role used to lease due envelopes.
    /// </summary>
    private readonly IInboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the store role used to record envelope execution results.
    /// </summary>
    private readonly IInboxStateStore _stateStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due envelopes.</param>
    /// <param name="stateStore">The store role used to record envelope execution results.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    public InboxProcessor(
        IInboxLeaseStore leaseStore,
        IInboxStateStore stateStore,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        if (_options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.BatchSize, "Batch size must be greater than zero.");
        }

        if (_options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.LeaseDuration, "Lease duration must be greater than zero.");
        }

        MessageProcessorDiagnostics.ValidateRetryOptions(_options.Retry, nameof(options));
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = InboxProcessorTelemetry.ActivitySource.StartActivity("inbox.processor.pass");

        var stopwatch = ValueStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration
        }, cancellationToken).ConfigureAwait(false);

        var completedIds = new List<Guid>(leasedEnvelopes.Count);
        var failures = new List<InboxEnvelopeFailure>();
        var deadLetters = new List<InboxEnvelopeDeadLetter>();

        foreach (var envelope in leasedEnvelopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessEnvelopeAsync(envelope, completedIds, failures, deadLetters, cancellationToken).ConfigureAwait(false);
        }

        if (completedIds.Count > 0)
        {
            await _stateStore.MarkCompletedAsync(completedIds, cancellationToken).ConfigureAwait(false);
        }

        if (failures.Count > 0)
        {
            await _stateStore.MarkFailedAsync(failures, cancellationToken).ConfigureAwait(false);
        }

        foreach (var deadLetter in deadLetters)
        {
            await _stateStore.MoveToDeadLetterAsync(deadLetter, cancellationToken).ConfigureAwait(false);
        }

        var elapsed = stopwatch.GetElapsedTime();
        var result = new ProcessorPassResult
        {
            LeasedCount = leasedEnvelopes.Count,
            SucceededCount = completedIds.Count,
            FailedCount = failures.Count,
            DeadLetteredCount = deadLetters.Count,
            ElapsedTime = elapsed
        };

        passActivity?.SetTag("litebus.inbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.inbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.inbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.inbox.dead_lettered_count", result.DeadLetteredCount);
        InboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }

    /// <summary>
    ///     Dispatches one leased envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="completedIds">The list that collects identifiers for batch completion.</param>
    /// <param name="failures">The list that collects failures for batch retry updates.</param>
    /// <param name="deadLetters">The list that collects dead-letter transitions applied individually.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch or the state update.</param>
    /// <returns>A task that represents the asynchronous dispatch and state update.</returns>
    private async Task ProcessEnvelopeAsync(
        InboxEnvelope envelope,
        List<Guid> completedIds,
        List<InboxEnvelopeFailure> failures,
        List<InboxEnvelopeDeadLetter> deadLetters,
        CancellationToken cancellationToken)
    {
        using var messageActivity = InboxProcessorTelemetry.ActivitySource.StartActivity("inbox.processor.message");
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await _dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            completedIds.Add(envelope.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordFailure(envelope, exception, failures, deadLetters);
        }
    }

    /// <summary>
    ///     Converts a dispatch failure into retry or dead-letter state collected for batch persistence.
    /// </summary>
    /// <param name="envelope">The envelope that failed during this attempt.</param>
    /// <param name="exception">The exception captured from dispatch.</param>
    /// <param name="failures">The list that collects failures for batch retry updates.</param>
    /// <param name="deadLetters">The list that collects dead-letter transitions.</param>
    private void RecordFailure(
        InboxEnvelope envelope,
        Exception exception,
        List<InboxEnvelopeFailure> failures,
        List<InboxEnvelopeDeadLetter> deadLetters)
    {
        var error = MessageProcessorDiagnostics.FormatError(exception);

        if (envelope.AttemptCount >= _options.Retry.MaxAttempts)
        {
            deadLetters.Add(new InboxEnvelopeDeadLetter
            {
                Id = envelope.Id,
                Reason = error
            });

            return;
        }

        failures.Add(new InboxEnvelopeFailure
        {
            Id = envelope.Id,
            Error = error,
            VisibleAfter = _clock.GetUtcNow().Add(CalculateRetryDelay(envelope.AttemptCount))
        });
    }

    /// <summary>
    ///     Calculates the next retry delay from the attempt count already recorded by the leasing operation.
    /// </summary>
    /// <param name="attemptCount">The current persisted attempt count for the envelope.</param>
    /// <returns>The delay to add to the current clock value before the envelope becomes visible again.</returns>
    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var retryOptions = _options.Retry;
        var initialDelayTicks = retryOptions.InitialDelay.Ticks;
        var delayTicks = retryOptions.Backoff == RetryBackoff.Fixed
            ? initialDelayTicks
            : initialDelayTicks * Math.Pow(2, Math.Max(0, attemptCount - 1));

        var delay = TimeSpan.FromTicks((long)Math.Min(delayTicks, retryOptions.MaxDelay.Ticks));

        if (!retryOptions.UseJitter || delay == TimeSpan.Zero)
        {
            return delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long)Math.Min(delay.Ticks * jitterFactor, retryOptions.MaxDelay.Ticks));
    }

    /// <summary>
    ///     Lightweight stopwatch used to measure processor pass duration without allocating <see cref="Stopwatch" />.
    /// </summary>
    private readonly struct ValueStopwatch
    {
        /// <summary>
        ///     The timestamp captured when the stopwatch started.
        /// </summary>
        private readonly long _startedTimestamp;

        /// <summary>
        ///     Starts a new stopwatch instance.
        /// </summary>
        /// <returns>The running stopwatch value.</returns>
        public static ValueStopwatch StartNew() => new(Environment.TickCount64);

        /// <summary>
        ///     Initializes a new instance of the <see cref="ValueStopwatch" /> struct.
        /// </summary>
        /// <param name="startedTimestamp">The tick count captured at start.</param>
        private ValueStopwatch(long startedTimestamp)
        {
            _startedTimestamp = startedTimestamp;
        }

        /// <summary>
        ///     Gets the elapsed time since the stopwatch was started.
        /// </summary>
        /// <returns>The elapsed duration.</returns>
        public TimeSpan GetElapsedTime()
        {
            var elapsedTicks = Environment.TickCount64 - _startedTimestamp;
            return TimeSpan.FromMilliseconds(elapsedTicks);
        }
    }
}
