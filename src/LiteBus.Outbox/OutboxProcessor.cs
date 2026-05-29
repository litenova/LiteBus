using System;
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
    private readonly TimeProvider _clock;
    private readonly IOutboxDispatcher _dispatcher;
    private readonly string _leaseOwner;
    private readonly OutboxProcessorOptions _options;
    private readonly IOutboxMessageLeaseStore _leaseStore;
    private readonly IOutboxMessageStateStore _stateStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due messages.</param>
    /// <param name="stateStore">The store role used to record publication results.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    public OutboxProcessor(
        IOutboxMessageLeaseStore leaseStore,
        IOutboxMessageStateStore stateStore,
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
        var now = _clock.GetUtcNow();
        var leasedMessages = await _leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration
        }, cancellationToken).ConfigureAwait(false);

        foreach (var message in leasedMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return new ProcessorPassResult
        {
            LeasedCount = leasedMessages.Count
        };
    }

    /// <summary>
    ///     Publishes one leased message envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="message">The leased outbox message returned by the store.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch or the state update.</param>
    private async Task ProcessMessageAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
            await _stateStore.MarkPublishedAsync(message.MessageId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkFailedAsync(message, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Converts a dispatcher failure into retry or dead-letter state.
    /// </summary>
    /// <param name="message">The outbox message that failed during this attempt.</param>
    /// <param name="exception">The exception captured from dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel the state update.</param>
    private Task MarkFailedAsync(OutboxMessageEnvelope message, Exception exception, CancellationToken cancellationToken)
    {
        var error = MessageProcessorDiagnostics.FormatError(exception);

        if (message.AttemptCount >= _options.Retry.MaxAttempts)
        {
            return _stateStore.MoveToDeadLetterAsync(new OutboxMessageDeadLetter
            {
                MessageId = message.MessageId,
                Reason = error
            }, cancellationToken);
        }

        var visibleAfter = _clock.GetUtcNow().Add(CalculateRetryDelay(message.AttemptCount));

        return _stateStore.MarkFailedAsync(new OutboxMessageFailure
        {
            MessageId = message.MessageId,
            Error = error,
            VisibleAfter = visibleAfter
        }, cancellationToken);
    }

    /// <summary>
    ///     Calculates the next retry delay from the attempt count already recorded by the leasing operation.
    /// </summary>
    /// <param name="attemptCount">The current persisted attempt count for the outbox message.</param>
    /// <returns>The delay to add to the current clock value before the message becomes visible again.</returns>
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
}