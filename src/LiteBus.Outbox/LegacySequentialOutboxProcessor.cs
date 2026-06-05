using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Sequential processor that leases outbox messages and publishes them one at a time through
///     <see cref="IOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     <para>
///         Each processing pass leases a bounded batch and calls <see cref="IOutboxDispatcher.DispatchAsync" /> per
///         envelope in a single-threaded foreach loop. Select this implementation through
///         <see cref="OutboxProcessorOptions.Architecture" /> when the pipelined model is not required.
///     </para>
///     <para>
///         Failures from <see cref="IOutboxDispatcher.DispatchAsync" /> are recorded through
///         <see cref="IOutboxStateWriter" />. Each terminal outcome is persisted immediately with
///         <see cref="CancellationToken.None" /> so a reclaimed lease cannot re-publish in-flight work.
///     </para>
/// </remarks>
public class LegacySequentialOutboxProcessor : IOutboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to publish each leased message.
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
    ///     Gets the processing store used to lease and persist envelope state transitions.
    /// </summary>
    private readonly IOutboxProcessingStore _processingStore;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<LegacySequentialOutboxProcessor> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LegacySequentialOutboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public LegacySequentialOutboxProcessor(
        IOutboxProcessingStore processingStore,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        ILogger<LegacySequentialOutboxProcessor>? logger = null)
    {
        _processingStore = processingStore ?? throw new ArgumentNullException(nameof(processingStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<LegacySequentialOutboxProcessor>.Instance;
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        OutboxProcessorFactory.ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity("outbox.processor.pass");

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _processingStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration,
            TenantId = _options.TenantId
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} outbox message(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        var accumulator = new ProcessorPassAccumulator<OutboxEnvelope>();

        try
        {
            foreach (var envelope in leasedEnvelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var updated = await OutboxProcessorEnvelopeHandler.ProcessAsync(
                    envelope,
                    _dispatcher,
                    _options,
                    _clock,
                    accumulator,
                    _logger,
                    cancellationToken).ConfigureAwait(false);

                if (updated is not null)
                {
                    await _processingStore.PersistAsync(new[] { updated }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (accumulator.TotalCount > 0)
            {
                await _processingStore.PersistAsync(accumulator.Updates, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return OutboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }
}
