using System;

using System.Diagnostics;

using System.Threading;

using System.Threading.Tasks;

using LiteBus.Messaging.Abstractions;

using LiteBus.Outbox.Abstractions;



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

///         Cancellation is allowed to escape so the host can stop without converting shutdown into a publication failure.

///         Any other exception from the dispatcher is treated as a failed publication attempt.

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

    ///     Gets the store role used to record publication results.

    /// </summary>

    private readonly IOutboxTerminalStateStore _stateStore;



    /// <summary>

    ///     Initializes a new instance of the <see cref="OutboxProcessor" /> class.

    /// </summary>

    /// <param name="leaseStore">The store role used to lease due messages.</param>

    /// <param name="stateStore">The store role used to record publication results.</param>

    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>

    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>

    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>

    public OutboxProcessor(

        IOutboxLeaseStore leaseStore,

        IOutboxTerminalStateStore stateStore,

        IOutboxDispatcher dispatcher,

        OutboxProcessorOptions options,

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

        using var passActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity("outbox.processor.pass");



        var stopwatch = ValueStopwatch.StartNew();

        var now = _clock.GetUtcNow();

        var leasedMessages = await _leaseStore.LeasePendingAsync(new OutboxLeaseRequest

        {

            BatchSize = _options.BatchSize,

            LeaseOwner = _leaseOwner,

            Now = now,

            LeaseDuration = _options.LeaseDuration

        }, cancellationToken).ConfigureAwait(false);



        var accumulator = new ProcessorPassAccumulator();



        foreach (var message in leasedMessages)

        {

            cancellationToken.ThrowIfCancellationRequested();

            await ProcessMessageAsync(message, accumulator, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.SucceededIds.Count > 0)

        {

            await _stateStore.MarkPublishedAsync(accumulator.SucceededIds, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.Failures.Count > 0)

        {

            await _stateStore.MarkFailedAsync(accumulator.Failures, cancellationToken).ConfigureAwait(false);

        }



        if (accumulator.DeadLetters.Count > 0)

        {

            await _stateStore.MoveToDeadLetterAsync(accumulator.DeadLetters, cancellationToken).ConfigureAwait(false);

        }



        var result = accumulator.ToResult(leasedMessages.Count, stopwatch.GetElapsedTime());



        passActivity?.SetTag("litebus.outbox.leased_count", result.LeasedCount);

        passActivity?.SetTag("litebus.outbox.succeeded_count", result.SucceededCount);

        passActivity?.SetTag("litebus.outbox.failed_count", result.FailedCount);

        passActivity?.SetTag("litebus.outbox.dead_lettered_count", result.DeadLetteredCount);

        OutboxProcessorTelemetry.RecordPass(

            result.LeasedCount,

            result.SucceededCount,

            result.FailedCount,

            result.DeadLetteredCount);



        return result;

    }



    /// <summary>

    ///     Publishes one leased message envelope and records its terminal state for this attempt.

    /// </summary>

    /// <param name="message">The leased outbox message returned by the store.</param>

    /// <param name="accumulator">The pass accumulator that collects outcomes for batch persistence.</param>

    /// <param name="cancellationToken">A token used to cancel dispatch or the state update.</param>

    /// <returns>A task that represents the asynchronous dispatch and state update.</returns>

    private async Task ProcessMessageAsync(

        OutboxEnvelope message,

        ProcessorPassAccumulator accumulator,

        CancellationToken cancellationToken)

    {

        MessageProcessorDiagnostics.TryGetParentActivityContext(message.TraceContext, out var parentContext);

        using var messageActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity(

            "outbox.processor.message",

            ActivityKind.Internal,

            parentContext);

        messageActivity?.SetTag("litebus.message_id", message.Id);



        try

        {

            await _dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);

            accumulator.RecordSuccess(message.Id);

        }

        catch (Exception exception) when (exception is not OperationCanceledException)

        {

            RecordFailure(message, exception, accumulator);

        }

    }



    /// <summary>

    ///     Converts a dispatcher failure into retry or dead-letter state collected for batch persistence.

    /// </summary>

    /// <param name="message">The outbox message that failed during this attempt.</param>

    /// <param name="exception">The exception captured from dispatch.</param>

    /// <param name="accumulator">The pass accumulator that collects outcomes for batch persistence.</param>

    private void RecordFailure(

        OutboxEnvelope message,

        Exception exception,

        ProcessorPassAccumulator accumulator)

    {

        var error = MessageProcessorDiagnostics.FormatError(exception);



        if (message.AttemptCount >= _options.Retry.MaxAttempts)

        {

            accumulator.RecordDeadLetter(new OutboxEnvelopeDeadLetter

            {

                Id = message.Id,

                Reason = error

            });



            return;

        }



        accumulator.RecordFailure(new OutboxEnvelopeFailure

        {

            Id = message.Id,

            Error = error,

            VisibleAfter = _clock.GetUtcNow().Add(_options.Retry.CalculateDelay(message.AttemptCount))

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


