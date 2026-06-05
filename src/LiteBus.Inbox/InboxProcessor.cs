using System;

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

///         <see cref="IInboxTerminalStateStore" />. Completion failures are not converted into retry state because dispatch

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

    private readonly IInboxTerminalStateStore _stateStore;



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

        IInboxTerminalStateStore stateStore,

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



        var accumulator = new ProcessorPassAccumulator();



        foreach (var envelope in leasedEnvelopes)

        {

            cancellationToken.ThrowIfCancellationRequested();

            await ProcessEnvelopeAsync(envelope, accumulator, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.SucceededIds.Count > 0)

        {

            await _stateStore.MarkCompletedAsync(accumulator.SucceededIds, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.Failures.Count > 0)

        {

            await _stateStore.MarkFailedAsync(accumulator.Failures, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.DeadLetters.Count > 0)

        {

            await _stateStore.MoveToDeadLetterAsync(accumulator.DeadLetters, cancellationToken).ConfigureAwait(false);

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



        return result;

    }



    /// <summary>

    ///     Dispatches one leased envelope and records its terminal state for this attempt.

    /// </summary>

    /// <param name="envelope">The leased envelope returned by the store.</param>

    /// <param name="accumulator">The pass accumulator that collects outcomes for batch persistence.</param>

    /// <param name="cancellationToken">A token used to cancel dispatch or the state update.</param>

    /// <returns>A task that represents the asynchronous dispatch and state update.</returns>

    private async Task ProcessEnvelopeAsync(

        InboxEnvelope envelope,

        ProcessorPassAccumulator accumulator,

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

            accumulator.RecordSuccess(envelope.Id);

        }

        catch (Exception exception) when (exception is not OperationCanceledException)

        {

            RecordFailure(envelope, exception, accumulator);

        }

    }



    /// <summary>

    ///     Converts a dispatch failure into retry or dead-letter state collected for batch persistence.

    /// </summary>

    /// <param name="envelope">The envelope that failed during this attempt.</param>

    /// <param name="exception">The exception captured from dispatch.</param>

    /// <param name="accumulator">The pass accumulator that collects outcomes for batch persistence.</param>

    private void RecordFailure(

        InboxEnvelope envelope,

        Exception exception,

        ProcessorPassAccumulator accumulator)

    {

        var error = MessageProcessorDiagnostics.FormatError(exception);



        if (envelope.AttemptCount >= _options.Retry.MaxAttempts)

        {

            accumulator.RecordDeadLetter(new InboxEnvelopeDeadLetter

            {

                Id = envelope.Id,

                Reason = error

            });



            return;

        }



        accumulator.RecordFailure(new InboxEnvelopeFailure

        {

            Id = envelope.Id,

            Error = error,

            VisibleAfter = _clock.GetUtcNow().Add(_options.Retry.CalculateDelay(envelope.AttemptCount))

        });

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


