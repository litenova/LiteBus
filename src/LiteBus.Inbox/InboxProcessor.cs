using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
///         <see cref="IInboxStateWriter" />. Each terminal outcome is persisted immediately with
///         <see cref="CancellationToken.None" /> so a reclaimed lease cannot re-dispatch in-flight work. A
///         <c>finally</c> block persists any accumulated updates with <see cref="CancellationToken.None" /> so
///         cancellation mid-pass cannot discard completed outcomes.
///     </para>
///     <para>
///         Cancellation is allowed to escape from dispatch so the host can stop without converting shutdown into a
///         failure record.
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
    ///     Gets the store role used to persist post-transition envelopes.
    /// </summary>
    private readonly IInboxStateWriter _stateWriter;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<InboxProcessor> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due envelopes.</param>
    /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public InboxProcessor(
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        ILogger<InboxProcessor>? logger = null)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<InboxProcessor>.Instance;
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

        _logger.LogDebug(
            "Leased {LeasedCount} inbox envelope(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        var accumulator = new ProcessorPassAccumulator<InboxEnvelope>();

        try
        {
            foreach (var envelope in leasedEnvelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var updated = await ProcessEnvelopeAsync(envelope, accumulator, cancellationToken).ConfigureAwait(false);

                if (updated is not null)
                {
                    await _stateWriter.PersistAsync(new[] { updated }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (accumulator.TotalCount > 0)
            {
                await _stateWriter.PersistAsync(accumulator.Updates, CancellationToken.None).ConfigureAwait(false);
            }
        }

        var result = accumulator.ToResult(leasedEnvelopes.Count, stopwatch.GetElapsedTime());

        passActivity?.SetTag("litebus.inbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.inbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.inbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.inbox.dead_lettered_count", result.DeadLetteredCount);
        InboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        _logger.LogInformation(
            "Inbox pass completed in {ElapsedMilliseconds} ms. Leased={LeasedCount}, Succeeded={SucceededCount}, Failed={FailedCount}, DeadLettered={DeadLetteredCount}.",
            result.ElapsedTime.TotalMilliseconds,
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
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    private async Task<InboxEnvelope?> ProcessEnvelopeAsync(
        InboxEnvelope envelope,
        ProcessorPassAccumulator<InboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = InboxProcessorTelemetry.ActivitySource.StartActivity(
            "inbox.processor.message",
            ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await _dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsCompleted();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            _logger.LogWarning(
                exception,
                "Inbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            InboxEnvelope updated;
            if (envelope.AttemptCount >= _options.Retry.MaxAttempts)
            {
                updated = envelope.AsDeadLettered(error);
                accumulator.RecordDeadLettered(updated);
            }
            else
            {
                var visibleAfter = _clock.GetUtcNow().Add(_options.Retry.CalculateDelay(envelope.AttemptCount));
                updated = envelope.AsFailed(error, visibleAfter);
                accumulator.RecordFailed(updated);
            }

            return updated;
        }
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
