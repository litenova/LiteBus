using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default processor that leases outbox messages and publishes them through an outbox dispatcher.
/// </summary>
/// <remarks>
///     <para>
///         Each processing pass leases a bounded batch, sends each envelope to <see cref="IOutboxDispatcher" />, then
///         records published, retry, or dead-letter state. The processor owns retry timing so stores stay focused on data
///         access and state transitions.
///     </para>
///     <para>
///         Each terminal outcome is persisted immediately with <see cref="CancellationToken.None" /> so a reclaimed
///         lease cannot re-publish in-flight work. A <c>finally</c> block persists any accumulated updates with
///         <see cref="CancellationToken.None" /> so cancellation mid-pass cannot discard completed outcomes.
///     </para>
///     <para>
///         Cancellation is allowed to escape from dispatch so the host can stop without converting shutdown into a
///         publication failure. Any other exception from the dispatcher is treated as a failed publication attempt.
///     </para>
/// </remarks>
public sealed class OutboxProcessor : IOutboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to publish leased messages.
    /// </summary>
    private readonly IOutboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the lease owner name assigned to messages claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly OutboxProcessorOptions _options;

    /// <summary>
    ///     Gets the store role used to lease due messages.
    /// </summary>
    private readonly IOutboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the store role used to persist post-transition envelopes.
    /// </summary>
    private readonly IOutboxStateWriter _stateWriter;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<OutboxProcessor> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due messages.</param>
    /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public OutboxProcessor(
        IOutboxLeaseStore leaseStore,
        IOutboxStateWriter stateWriter,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        ILogger<OutboxProcessor>? logger = null)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<OutboxProcessor>.Instance;
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
        using var passActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity("outbox.processor.pass");

        var stopwatch = ValueStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leased = await _leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} outbox message(s) as owner {LeaseOwner}.",
            leased.Count,
            _leaseOwner);

        var accumulator = new ProcessorPassAccumulator<OutboxEnvelope>();

        try
        {
            foreach (var envelope in leased)
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

        var result = accumulator.ToResult(leased.Count, stopwatch.GetElapsedTime());

        passActivity?.SetTag("litebus.outbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.outbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.outbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.outbox.dead_lettered_count", result.DeadLetteredCount);
        OutboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        _logger.LogInformation(
            "Outbox pass completed in {ElapsedMilliseconds} ms. Leased={LeasedCount}, Published={PublishedCount}, Failed={FailedCount}, DeadLettered={DeadLetteredCount}.",
            result.ElapsedTime.TotalMilliseconds,
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }

    /// <summary>
    ///     Publishes one leased message envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased outbox message returned by the store.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    private async Task<OutboxEnvelope?> ProcessEnvelopeAsync(
        OutboxEnvelope envelope,
        ProcessorPassAccumulator<OutboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity(
            "outbox.processor.message",
            ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await _dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsPublished();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            _logger.LogWarning(
                exception,
                "Outbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            OutboxEnvelope updated;
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
